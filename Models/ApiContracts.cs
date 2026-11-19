namespace Backend.Models;

// Requests. Times from the browser are UTC ISO strings (the slot list hands them out), never clinic-local text.

public sealed record BookingRequest(int ServiceId, int PractitionerId, DateTime StartsAt, string? Name, string? Email, string? Phone, string? Notes);

public sealed record RescheduleRequest(DateTime StartsAt);

public sealed record CancelRequest(string? Reason);

public sealed record StaffStatusRequest(string Status, string? Reason);

/// <summary>Time off in clinic-local wall time (what staff type), for example 2026-12-01 13:00 to 17:00.</summary>
public sealed record TimeOffRequest(int PractitionerId, DateOnly Day, TimeOnly From, TimeOnly To, string? Reason);

// Responses.

/// <summary>Every error the API returns: a stable code for the frontend, a sentence for people, and field errors on a form.</summary>
public sealed record ApiError(string Code, string Message, IReadOnlyDictionary<string, string>? Fields = null);

public sealed record SlotDto(DateTime StartsAt, string LocalTime);

public sealed record DaySlotsDto(DateOnly Date, IReadOnlyList<SlotDto> Slots);

public sealed record DayCountDto(DateOnly Date, int FreeSlots);

public sealed record AppointmentDto(
    int Id,
    string Status,
    DateTime StartsAt,
    DateTime EndsAt,
    string LocalDate,
    string LocalTime,
    ClinicService Service,
    int PractitionerId,
    string PractitionerName,
    string PatientName,
    bool CanChange,
    DateTime ChangeDeadline);

public sealed record BookingResultDto(AppointmentDto Appointment, string ManageToken);

public sealed record StaffAppointmentDto(
    int Id, string Status, string LocalTime, string LocalEndTime, int DurationMinutes, string ServiceName,
    string PatientName, string PatientEmail, string PatientPhone, string? Notes, int RescheduleCount);

public sealed record StaffTimeOffDto(int Id, string From, string To, string? Reason);

public sealed record StaffColumnDto(int PractitionerId, string PractitionerName, IReadOnlyList<StaffAppointmentDto> Appointments, IReadOnlyList<StaffTimeOffDto> TimeOff);

public sealed record StaffDayDto(DateOnly Date, IReadOnlyList<StaffColumnDto> Practitioners);
