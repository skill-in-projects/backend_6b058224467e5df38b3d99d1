using Backend.Domain;
using Backend.Filters;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>The front desk: the day view, appointment statuses and time off. Every action needs the staff key.</summary>
[ApiController]
[Route("api/staff")]
[StaffKey]
public class StaffController : ControllerBase
{
    private readonly CatalogStore _catalog;
    private readonly AppointmentStore _appointments;
    private readonly ScheduleStore _schedule;
    private readonly ClinicOptions _options;
    private readonly TimeProvider _time;

    public StaffController(CatalogStore catalog, AppointmentStore appointments, ScheduleStore schedule, ClinicOptions options, TimeProvider time)
    {
        _catalog = catalog;
        _appointments = appointments;
        _schedule = schedule;
        _options = options;
        _time = time;
    }

    /// <summary>GET api/staff/day?date=2026-12-01: one column per practitioner, every appointment that day and the time off.</summary>
    [HttpGet("day")]
    public async Task<IActionResult> Day([FromQuery] DateOnly date, CancellationToken ct)
    {
        var zone = _options.TimeZone();
        var range = ClinicClock.DayRange(date, zone);
        var practitioners = await _catalog.PractitionersAsync(null, ct);
        var services = (await _catalog.ServicesAsync(ct)).ToDictionary(s => s.Id);
        var appointments = await _appointments.InRangeAsync(range, ct);
        var timeOff = await _schedule.TimeOffAsync(range, ct);
        string Local(DateTime utc) => ClinicClock.ToLocal(utc, zone).ToString("HH:mm");

        var columns = practitioners.Select(p => new StaffColumnDto(
            p.Id,
            p.Name,
            appointments.Where(a => a.PractitionerId == p.Id).Select(a => new StaffAppointmentDto(
                a.Id, a.Status, Local(a.StartsAt), Local(a.EndsAt), (int)(a.EndsAt - a.StartsAt).TotalMinutes,
                services.TryGetValue(a.ServiceId, out var s) ? s.Name : "Unknown service",
                a.PatientName, a.PatientEmail, a.PatientPhone, a.Notes, a.RescheduleCount)).ToList(),
            timeOff.Where(t => t.PractitionerId == p.Id).Select(t => new StaffTimeOffDto(t.Id, Local(t.StartsAt), Local(t.EndsAt), t.Reason)).ToList()))
            .ToList();
        return Ok(new StaffDayDto(date, columns));
    }

    [HttpPost("appointments/{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] StaffStatusRequest request, CancellationToken ct)
    {
        var appointment = await _appointments.ByIdAsync(id, ct);
        if (appointment is null) return this.Error(new ApiError("no_appointment", "That appointment does not exist."));

        var rule = BookingRules.StaffCanSet(appointment.Status, request.Status, appointment.StartsAt, _time.GetUtcNow().UtcDateTime);
        if (!rule.Ok) return this.Error(new ApiError(rule.Code!, rule.Message!));

        var reason = request.Status == AppointmentStatus.Cancelled ? (string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled by the clinic" : request.Reason.Trim()) : null;
        var updated = await _appointments.SetStatusAsync(id, appointment.Status, request.Status, reason, ct);
        if (updated is null) return this.Error(new ApiError("not_active", "Someone changed this appointment a moment ago. Reload the day."));
        return Ok(new { updated.Id, updated.Status });
    }

    [HttpPost("time-off")]
    public async Task<IActionResult> AddTimeOff([FromBody] TimeOffRequest request, CancellationToken ct)
    {
        if (await _catalog.PractitionerAsync(request.PractitionerId, ct) is null)
            return this.Error(new ApiError("no_practitioner", "That practitioner does not exist."));
        if (request.To <= request.From)
            return BadRequest(new ApiError("bad_range", "The end time must be after the start time."));

        var zone = _options.TimeZone();
        var from = ClinicClock.ToUtc(request.Day, request.From, zone);
        var to = ClinicClock.ToUtc(request.Day, request.To, zone);
        if (from is null || to is null) return BadRequest(new ApiError("bad_time", "That time does not exist on this day (the clocks change)."));

        var (added, clashes) = await _schedule.AddTimeOffAsync(request.PractitionerId, new TimeRange(from.Value, to.Value), request.Reason?.Trim(), ct);
        if (added is null)
        {
            var list = string.Join(", ", clashes.Select(c => $"{ClinicClock.ToLocal(c.StartsAt, zone):HH:mm} {c.PatientName}"));
            return this.Error(new ApiError("time_off_clash", $"These appointments are booked in that time: {list}. Contact the patients and cancel them first."));
        }
        return Ok(added);
    }

    [HttpDelete("time-off/{id:int}")]
    public async Task<IActionResult> DeleteTimeOff(int id, CancellationToken ct) =>
        await _schedule.DeleteTimeOffAsync(id, ct) ? NoContent() : NotFound(new ApiError("no_time_off", "That time off does not exist."));
}
