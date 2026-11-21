namespace Backend.Domain;

public static class AppointmentStatus
{
    public const string Booked = "booked";
    public const string Cancelled = "cancelled";
    public const string Completed = "completed";
    public const string NoShow = "no_show";

    /// <summary>Statuses staff can set from the day view. Booked is never set by hand: a booking goes through the slot check.</summary>
    public static readonly string[] SetByStaff = { Cancelled, Completed, NoShow };
}

/// <summary>Result of a rule check: Ok, or a stable code the frontend maps to its own message.</summary>
public readonly record struct RuleResult(bool Ok, string? Code = null, string? Message = null)
{
    public static RuleResult Pass => new(true);
    public static RuleResult Fail(string code, string message) => new(false, code, message);
}

/// <summary>What a patient may do with their own appointment. Staff are not bound by the notice period.</summary>
public static class BookingRules
{
    public static DateTime ChangeDeadline(DateTime startsAtUtc, ClinicOptions options) =>
        startsAtUtc.AddHours(-options.CancellationNoticeHours);

    public static RuleResult PatientCanChange(string status, DateTime startsAtUtc, DateTime nowUtc, ClinicOptions options)
    {
        if (status != AppointmentStatus.Booked)
            return RuleResult.Fail("not_active", "This appointment is no longer active.");
        if (nowUtc >= ChangeDeadline(startsAtUtc, options))
            return RuleResult.Fail("too_late",
                $"Appointments can be changed online until {options.CancellationNoticeHours} hours before they start. Please call the clinic.");
        return RuleResult.Pass;
    }

    public static RuleResult StaffCanSet(string currentStatus, string newStatus, DateTime startsAtUtc, DateTime nowUtc)
    {
        if (!AppointmentStatus.SetByStaff.Contains(newStatus))
            return RuleResult.Fail("bad_status", "Unknown status.");
        if (currentStatus == AppointmentStatus.Cancelled)
            return RuleResult.Fail("not_active", "This appointment was cancelled.");
        // Completed and no-show describe something that happened: they cannot be set before the appointment starts.
        if (newStatus is AppointmentStatus.Completed or AppointmentStatus.NoShow && nowUtc < startsAtUtc)
            return RuleResult.Fail("not_started", "This appointment has not started yet.");
        return RuleResult.Pass;
    }
}
