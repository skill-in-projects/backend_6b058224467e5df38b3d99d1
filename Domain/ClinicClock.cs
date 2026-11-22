namespace Backend.Domain;

/// <summary>Converts between clinic-local wall time and UTC. Daylight saving is handled here and nowhere else.</summary>
public static class ClinicClock
{
    /// <summary>Null when the local time does not exist (the hour skipped when the clocks go forward). For the repeated
    /// hour when the clocks go back, the first occurrence (daylight time) is used, which is what a patient reading a
    /// clock expects.</summary>
    public static DateTime? ToUtc(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) return null;
        if (zone.IsAmbiguousTime(local))
        {
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    public static DateOnly LocalDay(DateTime utc, TimeZoneInfo zone) => DateOnly.FromDateTime(ToLocal(utc, zone));

    /// <summary>The UTC range covering one clinic-local calendar day (23 or 25 hours on a clock-change day).</summary>
    public static TimeRange DayRange(DateOnly day, TimeZoneInfo zone)
    {
        var start = ToUtc(day, TimeOnly.MinValue, zone) ?? ToUtc(day, new TimeOnly(1, 0), zone)!.Value;
        var next = day.AddDays(1);
        var end = ToUtc(next, TimeOnly.MinValue, zone) ?? ToUtc(next, new TimeOnly(1, 0), zone)!.Value;
        return new TimeRange(start, end);
    }
}
