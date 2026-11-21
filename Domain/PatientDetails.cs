using System.Net.Mail;

namespace Backend.Domain;

/// <summary>The patient fields on a booking, trimmed, with per-field errors for the form.</summary>
public sealed record PatientDetails(string Name, string Email, string Phone, string? Notes)
{
    public const int MaxNotes = 500;

    public static PatientDetails Normalize(string? name, string? email, string? phone, string? notes) => new(
        (name ?? "").Trim(),
        (email ?? "").Trim().ToLowerInvariant(),
        (phone ?? "").Trim(),
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());

    public Dictionary<string, string> Validate()
    {
        var errors = new Dictionary<string, string>();
        if (Name.Length < 2 || Name.Length > 100) errors["name"] = "Enter your full name.";
        if (!IsEmail(Email)) errors["email"] = "Enter a valid email address.";
        var digits = Phone.Count(char.IsDigit);
        if (digits < 7 || digits > 15 || Phone.Any(c => !(char.IsDigit(c) || c is ' ' or '+' or '-' or '(' or ')')))
            errors["phone"] = "Enter a phone number we can call.";
        if (Notes is { Length: > MaxNotes }) errors["notes"] = $"Keep notes under {MaxNotes} characters.";
        return errors;
    }

    private static bool IsEmail(string value)
    {
        if (value.Length is < 3 or > 254 || value.Contains(' ')) return false;
        try
        {
            var address = new MailAddress(value);
            return address.Address == value && address.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
