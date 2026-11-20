using Backend.Domain;
using Backend.Models;

namespace Backend.Services;

/// <summary>Free slots for the booking screens: one day in detail, or a count per day for the date strip.</summary>
public sealed class AvailabilityService
{
    private readonly ScheduleStore _schedule;
    private readonly ClinicOptions _options;
    private readonly TimeProvider _time;

    public AvailabilityService(ScheduleStore schedule, ClinicOptions options, TimeProvider time)
    {
        _schedule = schedule;
        _options = options;
        _time = time;
    }

    public async Task<DaySlotsDto> DayAsync(ClinicService service, int practitionerId, IReadOnlyList<WorkingWindow> hours, DateOnly day, CancellationToken ct)
    {
        var zone = _options.TimeZone();
        var busy = await _schedule.BusyAsync(practitionerId, ClinicClock.DayRange(day, zone), ct);
        var slots = Free(service, hours, busy, day, zone);
        return new DaySlotsDto(day, slots.Select(s => new SlotDto(s, ClinicClock.ToLocal(s, zone).ToString("HH:mm"))).ToList());
    }

    /// <summary>One query for the whole range, then the calculator per day: the date strip needs counts, not 14 round trips.</summary>
    public async Task<IReadOnlyList<DayCountDto>> DaysAsync(ClinicService service, int practitionerId, IReadOnlyList<WorkingWindow> hours,
        DateOnly from, int days, CancellationToken ct)
    {
        var zone = _options.TimeZone();
        var range = new TimeRange(ClinicClock.DayRange(from, zone).Start, ClinicClock.DayRange(from.AddDays(days - 1), zone).End);
        var busy = await _schedule.BusyAsync(practitionerId, range, ct);
        return Enumerable.Range(0, days)
            .Select(i => from.AddDays(i))
            .Select(d => new DayCountDto(d, Free(service, hours, busy, d, zone).Count))
            .ToList();
    }

    public DateOnly Today() => ClinicClock.LocalDay(_time.GetUtcNow().UtcDateTime, _options.TimeZone());

    private IReadOnlyList<DateTime> Free(ClinicService service, IReadOnlyList<WorkingWindow> hours, IReadOnlyList<TimeRange> busy, DateOnly day, TimeZoneInfo zone) =>
        SlotCalculator.FreeSlots(day, new SlotService(service.DurationMinutes, service.BufferMinutes), hours, busy,
            _time.GetUtcNow().UtcDateTime, _options, zone);
}
