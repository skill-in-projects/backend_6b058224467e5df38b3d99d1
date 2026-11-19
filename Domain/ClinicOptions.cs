namespace Backend.Domain;

/// <summary>Clinic-wide booking rules. Bound from the "Clinic" section of appsettings.json; the defaults are the clinic's
/// current policy.</summary>
public sealed class ClinicOptions
{
    public string Name { get; set; } = "Harbor Street Clinic";

    /// <summary>IANA id. Working hours are entered in clinic time; everything stored is UTC.</summary>
    public string TimeZoneId { get; set; } = "Asia/Jerusalem";

    /// <summary>Slots start on this grid (09:00, 09:15, ...).</summary>
    public int SlotStepMinutes { get; set; } = 15;

    /// <summary>A slot must start at least this long from now, so the clinic is never surprised by a booking.</summary>
    public int MinLeadMinutes { get; set; } = 120;

    /// <summary>How far ahead patients can book.</summary>
    public int BookingHorizonDays { get; set; } = 60;

    /// <summary>Patients can cancel or move an appointment until this many hours before it starts.</summary>
    public int CancellationNoticeHours { get; set; } = 24;

    public TimeZoneInfo TimeZone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}
