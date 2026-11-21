using Backend.Domain;

namespace Tests;

public class PatientDetailsTests
{
    [Fact]
    public void Valid_details_are_trimmed_and_the_email_lowercased()
    {
        var p = PatientDetails.Normalize("  Dana Mizrahi ", " Dana.M@Example.COM ", " +972 50-123-4567 ", "   ");

        Assert.Empty(p.Validate());
        Assert.Equal("Dana Mizrahi", p.Name);
        Assert.Equal("dana.m@example.com", p.Email);
        Assert.Null(p.Notes);
    }

    [Theory]
    [InlineData("a", "dana@example.com", "0501234567", "name")]
    [InlineData("Dana", "dana@example", "0501234567", "email")]
    [InlineData("Dana", "dana example.com", "0501234567", "email")]
    [InlineData("Dana", "dana@example.com", "12345", "phone")]
    [InlineData("Dana", "dana@example.com", "050-CALL-ME", "phone")]
    public void Each_bad_field_is_reported_by_name(string name, string email, string phone, string field)
    {
        var errors = PatientDetails.Normalize(name, email, phone, null).Validate();

        Assert.Equal(new[] { field }, errors.Keys);
    }

    [Fact]
    public void Long_notes_are_refused()
    {
        var errors = PatientDetails.Normalize("Dana", "dana@example.com", "0501234567", new string('x', 501)).Validate();

        Assert.True(errors.ContainsKey("notes"));
    }
}
