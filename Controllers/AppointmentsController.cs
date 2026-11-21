using Backend.Domain;
using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// The patient side: book, then manage through the secret link (no account). The link token is never logged or
/// returned again after the booking response.
/// </summary>
[ApiController]
[Route("api/appointments")]
public class AppointmentsController : ControllerBase
{
    private static readonly ApiError NotFoundError = new("no_appointment", "We could not find that appointment. Check the link in your confirmation.");

    private readonly CatalogStore _catalog;
    private readonly AppointmentStore _appointments;
    private readonly AppointmentView _view;
    private readonly ClinicOptions _options;
    private readonly TimeProvider _time;

    public AppointmentsController(CatalogStore catalog, AppointmentStore appointments, AppointmentView view, ClinicOptions options, TimeProvider time)
    {
        _catalog = catalog;
        _appointments = appointments;
        _view = view;
        _options = options;
        _time = time;
    }

    [HttpPost]
    public async Task<IActionResult> Book([FromBody] BookingRequest request, CancellationToken ct)
    {
        var patient = PatientDetails.Normalize(request.Name, request.Email, request.Phone, request.Notes);
        var fields = patient.Validate();
        if (fields.Count > 0) return BadRequest(new ApiError("invalid", "Please check the highlighted fields.", fields));
        if (ApiErrors.Utc(request.StartsAt) is not { } startsAt)
            return BadRequest(new ApiError("bad_time", "The start time must include its time zone."));

        var (service, practitioner, error) = await _catalog.OfferAsync(request.ServiceId, request.PractitionerId, ct);
        if (error != null) return this.Error(error);

        var hours = await _catalog.WorkingHoursAsync(practitioner!.Id, ct);
        var outcome = await _appointments.BookAsync(service!, practitioner.Id, startsAt, patient, hours, ct);
        if (outcome.ErrorCode != null) return this.Error(SlotTaken);

        var dto = _view.For(outcome.Appointment!, service!, practitioner);
        return Created($"/api/appointments/manage", new BookingResultDto(dto, outcome.ManageToken!));
    }

    [HttpGet("manage/{token}")]
    public async Task<IActionResult> Get(string token, CancellationToken ct)
    {
        var found = await LoadAsync(token, ct);
        if (found is null) return NotFound(NotFoundError);
        var (appointment, service, practitioner) = found.Value;
        return Ok(_view.For(appointment, service, practitioner));
    }

    [HttpPost("manage/{token}/cancel")]
    public async Task<IActionResult> Cancel(string token, [FromBody] CancelRequest request, CancellationToken ct)
    {
        var found = await LoadAsync(token, ct);
        if (found is null) return NotFound(NotFoundError);
        var (appointment, service, practitioner) = found.Value;

        var rule = BookingRules.PatientCanChange(appointment.Status, appointment.StartsAt, _time.GetUtcNow().UtcDateTime, _options);
        if (!rule.Ok) return this.Error(new ApiError(rule.Code!, rule.Message!));

        var given = request.Reason?.Trim();
        var reason = string.IsNullOrEmpty(given) ? "Cancelled by the patient" : given.Length > 200 ? given[..200] : given;
        var cancelled = await _appointments.SetStatusAsync(appointment.Id, AppointmentStatus.Booked, AppointmentStatus.Cancelled, reason, ct);
        if (cancelled is null) return this.Error(new ApiError("not_active", "This appointment is no longer active."));
        return Ok(_view.For(cancelled, service, practitioner));
    }

    [HttpPost("manage/{token}/reschedule")]
    public async Task<IActionResult> Reschedule(string token, [FromBody] RescheduleRequest request, CancellationToken ct)
    {
        var found = await LoadAsync(token, ct);
        if (found is null) return NotFound(NotFoundError);
        var (appointment, service, practitioner) = found.Value;
        if (ApiErrors.Utc(request.StartsAt) is not { } startsAt)
            return BadRequest(new ApiError("bad_time", "The start time must include its time zone."));

        var rule = BookingRules.PatientCanChange(appointment.Status, appointment.StartsAt, _time.GetUtcNow().UtcDateTime, _options);
        if (!rule.Ok) return this.Error(new ApiError(rule.Code!, rule.Message!));

        var hours = await _catalog.WorkingHoursAsync(practitioner.Id, ct);
        var outcome = await _appointments.RescheduleAsync(appointment, service, startsAt, hours, ct);
        if (outcome.ErrorCode == "not_active") return this.Error(new ApiError("not_active", "This appointment is no longer active."));
        if (outcome.ErrorCode != null) return this.Error(SlotTaken);
        return Ok(_view.For(outcome.Appointment!, service, practitioner));
    }

    private static readonly ApiError SlotTaken = new(BookingOutcome.SlotTaken, "Sorry, someone just took that time. Please pick another one.");

    private async Task<(Appointment, ClinicService, Practitioner)?> LoadAsync(string token, CancellationToken ct)
    {
        if (!ManageToken.LooksValid(token)) return null;
        var appointment = await _appointments.ByTokenAsync(token, ct);
        if (appointment is null) return null;
        var service = await _catalog.ServiceAsync(appointment.ServiceId, ct);
        var practitioner = await _catalog.PractitionerAsync(appointment.PractitionerId, ct);
        if (service is null || practitioner is null) return null;
        return (appointment, service, practitioner);
    }
}
