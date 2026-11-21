using Backend.Domain;
using Backend.Models;

namespace Backend.Services;

/// <summary>Turns an appointment row into what the patient screens show: clinic-local date and time, and whether they
/// can still change it themselves.</summary>
public sealed class AppointmentView
{
    private readonly ClinicOptions _options;
    private readonly TimeProvider _time;

    public AppointmentView(ClinicOptions options, TimeProvider time)
    {
        _options = options;
        _time = time;
    }

    public AppointmentDto For(Appointment a, ClinicService service, Practitioner practitioner)
    {
        var local = ClinicClock.ToLocal(a.StartsAt, _options.TimeZone());
        var canChange = BookingRules.PatientCanChange(a.Status, a.StartsAt, _time.GetUtcNow().UtcDateTime, _options).Ok;
        return new AppointmentDto(a.Id, a.Status, a.StartsAt, a.EndsAt, local.ToString("yyyy-MM-dd"), local.ToString("HH:mm"),
            service, practitioner.Id, practitioner.Name, a.PatientName, canChange, BookingRules.ChangeDeadline(a.StartsAt, _options));
    }
}
