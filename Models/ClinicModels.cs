namespace Backend.Models;

public sealed record Practitioner(int Id, string Name, string Title, string Bio, IReadOnlyList<int> ServiceIds);

public sealed record ClinicService(int Id, string Name, string Description, int DurationMinutes, int BufferMinutes, int PriceCents);

public sealed record TimeOff(int Id, int PractitionerId, DateTime StartsAt, DateTime EndsAt, string? Reason);

public sealed record Appointment(
    int Id,
    int PractitionerId,
    int ServiceId,
    string PatientName,
    string PatientEmail,
    string PatientPhone,
    string? Notes,
    DateTime StartsAt,
    DateTime EndsAt,
    DateTime BlockedUntil,
    string Status,
    DateTime? CancelledAt,
    string? CancelReason,
    int RescheduleCount,
    DateTime CreatedAt);
