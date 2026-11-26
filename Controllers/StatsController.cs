using Backend.Domain;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>Totals for the minutes dashboard. Only sums and counts leave here: no patient names, contacts or notes.</summary>
[ApiController]
[Route("api/stats")]
public class StatsController : ControllerBase
{
    private const int MaxDays = 31;

    private readonly CatalogStore _catalog;
    private readonly AppointmentStore _appointments;
    private readonly ClinicOptions _options;
    private readonly TimeProvider _time;

    public StatsController(CatalogStore catalog, AppointmentStore appointments, ClinicOptions options, TimeProvider time)
    {
        _catalog = catalog;
        _appointments = appointments;
        _options = options;
        _time = time;
    }

    /// <summary>GET api/stats/minutes?from=2026-11-22&amp;days=7: booked minutes per clinic-local day, practitioner and
    /// service. Cancelled visits are counted apart and add no minutes.</summary>
    [HttpGet("minutes")]
    public async Task<IActionResult> Minutes([FromQuery] DateOnly? from, [FromQuery] int days = 7, CancellationToken ct = default)
    {
        var zone = _options.TimeZone();
        var first = from ?? ClinicClock.LocalDay(_time.GetUtcNow().UtcDateTime, zone);
        if (days < 1 || days > MaxDays) return BadRequest(new ApiError("bad_range", $"Choose between 1 and {MaxDays} days."));
        if (first.Year < 2000 || first.Year > 2100) return BadRequest(new ApiError("bad_range", "That date is out of range."));

        var last = first.AddDays(days - 1);
        var range = new TimeRange(ClinicClock.DayRange(first, zone).Start, ClinicClock.DayRange(last, zone).End);
        var all = await _appointments.InRangeAsync(range, ct);
        var live = all.Where(a => a.Status != AppointmentStatus.Cancelled)
            .Select(a => (a.PractitionerId, a.ServiceId, Day: ClinicClock.LocalDay(a.StartsAt, zone), Minutes: (int)(a.EndsAt - a.StartsAt).TotalMinutes))
            .ToList();

        var perDay = Enumerable.Range(0, days).Select(i => first.AddDays(i))
            .Select(d => new DayMinutesDto(d, live.Where(a => a.Day == d).Sum(a => a.Minutes), live.Count(a => a.Day == d)))
            .ToList();
        var practitioners = (await _catalog.PractitionersAsync(null, ct))
            .Select(p => new NamedMinutesDto(p.Name, live.Where(a => a.PractitionerId == p.Id).Sum(a => a.Minutes), live.Count(a => a.PractitionerId == p.Id)))
            .ToList();
        var services = (await _catalog.ServicesAsync(ct))
            .Select(s => new NamedMinutesDto(s.Name, live.Where(a => a.ServiceId == s.Id).Sum(a => a.Minutes), live.Count(a => a.ServiceId == s.Id)))
            .ToList();

        return Ok(new MinutesDashboardDto(first, last, live.Sum(a => a.Minutes), live.Count, all.Count - live.Count, perDay, practitioners, services));
    }
}
