namespace Backend.Domain;

/// <summary>One block of working time on a weekday, in clinic-local time.</summary>
public sealed record WorkingWindow(DayOfWeek Day, TimeOnly Start, TimeOnly End);

/// <summary>What a slot needs to know about the service: how long it takes and the cleanup time after it.</summary>
public sealed record SlotService(int DurationMinutes, int BufferMinutes);

/// <summary>
/// Works out which start times are free for one practitioner, one service, one clinic-local day. Pure: the caller passes
/// the busy ranges (booked appointments including their buffer, and time off), so the same code runs for the date strip,
/// the slot list and the final check inside the booking transaction.
/// </summary>
public static class SlotCalculator
{
    public static IReadOnlyList<DateTime> FreeSlots(
        DateOnly day,
        SlotService service,
        IEnumerable<WorkingWindow> hours,
        IReadOnlyCollection<TimeRange> busy,
        DateTime nowUtc,
        ClinicOptions options,
        TimeZoneInfo zone)
    {
        var earliest = nowUtc.AddMinutes(options.MinLeadMinutes);
        var lastDay = ClinicClock.LocalDay(nowUtc, zone).AddDays(options.BookingHorizonDays);
        if (day > lastDay) return Array.Empty<DateTime>();

        var duration = TimeSpan.FromMinutes(service.DurationMinutes);
        var step = TimeSpan.FromMinutes(options.SlotStepMinutes);
        var slots = new List<DateTime>();

        foreach (var window in hours.Where(h => h.Day == day.DayOfWeek).OrderBy(h => h.Start))
        {
            var windowEnd = ClinicClock.ToUtc(day, window.End, zone);
            if (windowEnd is null) continue;

            // Step through wall-clock times so slots stay on the grid (09:00, 09:15) on clock-change days too.
            for (var local = window.Start.ToTimeSpan(); local < window.End.ToTimeSpan(); local += step)
            {
                var start = ClinicClock.ToUtc(day, TimeOnly.FromTimeSpan(local), zone);
                if (start is null) continue; // the skipped hour
                var end = start.Value + duration;
                if (end > windowEnd) break;
                if (start < earliest) continue;

                var blocked = new TimeRange(start.Value, end.AddMinutes(service.BufferMinutes));
                if (busy.Any(b => b.Overlaps(blocked))) continue;
                slots.Add(start.Value);
            }
        }
        return slots.Distinct().OrderBy(s => s).ToList();
    }
}
