using Backend.Domain;
using Backend.Models;
using Npgsql;

namespace Backend.Services;

/// <summary>Outcome of a booking or a move: the appointment, or a code the controller turns into 409 / 400.</summary>
public sealed record BookingOutcome(Appointment? Appointment, string? ManageToken, string? ErrorCode)
{
    public const string SlotTaken = "slot_taken";
    public static BookingOutcome Taken => new(null, null, SlotTaken);
}

/// <summary>
/// Appointments. Every write that can clash (book, move, add time off) takes a per-practitioner lock inside its
/// transaction, re-reads what is busy and re-runs <see cref="SlotCalculator"/>, so two people clicking the same slot at
/// the same moment cannot both get it. The lock only queues writers for the same practitioner.
/// </summary>
public sealed class AppointmentStore
{
    private const int LockNamespace = 7341; // first key of pg_advisory_xact_lock(int, int): "appointments"
    private const string Columns = """
        id, practitioner_id, service_id, patient_name, patient_email, patient_phone, notes, starts_at, ends_at, blocked_until,
        status, cancelled_at, cancel_reason, reschedule_count, created_at
        """;

    private readonly Database _db;
    private readonly ClinicOptions _options;
    private readonly TimeProvider _time;

    public AppointmentStore(Database db, ClinicOptions options, TimeProvider time)
    {
        _db = db;
        _options = options;
        _time = time;
    }

    public static async Task LockPractitionerAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int practitionerId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@ns, @p)", conn, tx);
        cmd.Parameters.AddWithValue("ns", LockNamespace);
        cmd.Parameters.AddWithValue("p", practitionerId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<BookingOutcome> BookAsync(ClinicService service, int practitionerId, DateTime startsAt, PatientDetails patient,
        IReadOnlyList<WorkingWindow> hours, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await LockPractitionerAsync(conn, tx, practitionerId, ct);
        if (!await StillFreeAsync(conn, tx, service, practitionerId, startsAt, hours, null, ct)) return BookingOutcome.Taken;

        var token = ManageToken.Create();
        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO appointments (practitioner_id, service_id, patient_name, patient_email, patient_phone, notes, starts_at, ends_at, blocked_until, manage_token_hash)
            VALUES (@p, @s, @name, @email, @phone, @notes, @start, @end, @blocked, @hash)
            RETURNING {Columns}
            """, conn, tx);
        cmd.Parameters.AddWithValue("p", practitionerId);
        cmd.Parameters.AddWithValue("s", service.Id);
        cmd.Parameters.AddWithValue("name", patient.Name);
        cmd.Parameters.AddWithValue("email", patient.Email);
        cmd.Parameters.AddWithValue("phone", patient.Phone);
        cmd.Parameters.Add(new NpgsqlParameter<string?>("notes", patient.Notes));
        AddTimes(cmd, service, startsAt);
        cmd.Parameters.AddWithValue("hash", ManageToken.Hash(token));
        try
        {
            var appointment = await ReadOneAsync(cmd, ct);
            await tx.CommitAsync(ct);
            return new BookingOutcome(appointment, token, null);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == "appointments_one_live_start")
        {
            return BookingOutcome.Taken;
        }
    }

    /// <summary>Moves an appointment to a new start, same service and practitioner. The manage link keeps working.</summary>
    public async Task<BookingOutcome> RescheduleAsync(Appointment current, ClinicService service, DateTime startsAt,
        IReadOnlyList<WorkingWindow> hours, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await LockPractitionerAsync(conn, tx, current.PractitionerId, ct);
        if (!await StillFreeAsync(conn, tx, service, current.PractitionerId, startsAt, hours, current.Id, ct)) return BookingOutcome.Taken;

        await using var cmd = new NpgsqlCommand($"""
            UPDATE appointments SET starts_at = @start, ends_at = @end, blocked_until = @blocked, reschedule_count = reschedule_count + 1
            WHERE id = @id AND status = 'booked'
            RETURNING {Columns}
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", current.Id);
        AddTimes(cmd, service, startsAt);
        try
        {
            var moved = await ReadOneAsync(cmd, ct);
            if (moved is null) return new BookingOutcome(null, null, "not_active");
            await tx.CommitAsync(ct);
            return new BookingOutcome(moved, null, null);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == "appointments_one_live_start")
        {
            return BookingOutcome.Taken;
        }
    }

    public async Task<Appointment?> ByTokenAsync(string token, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand($"SELECT {Columns} FROM appointments WHERE manage_token_hash = @hash", conn);
        cmd.Parameters.AddWithValue("hash", ManageToken.Hash(token));
        return await ReadOneAsync(cmd, ct);
    }

    public async Task<Appointment?> ByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand($"SELECT {Columns} FROM appointments WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await ReadOneAsync(cmd, ct);
    }

    /// <summary>Sets a status. Only a booked appointment changes, so two staff clicks cannot undo a cancellation.</summary>
    public async Task<Appointment?> SetStatusAsync(int id, string fromStatus, string status, string? reason, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand($"""
            UPDATE appointments
            SET status = @status,
                cancelled_at = CASE WHEN @status = 'cancelled' THEN @now ELSE cancelled_at END,
                cancel_reason = CASE WHEN @status = 'cancelled' THEN @reason ELSE cancel_reason END
            WHERE id = @id AND status = @from
            RETURNING {Columns}
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("from", fromStatus);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("now", _time.GetUtcNow().UtcDateTime);
        cmd.Parameters.Add(new NpgsqlParameter<string?>("reason", reason));
        return await ReadOneAsync(cmd, ct);
    }

    /// <summary>Every appointment (any status) starting inside the range, for the staff day view.</summary>
    public async Task<IReadOnlyList<Appointment>> InRangeAsync(TimeRange range, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand($"SELECT {Columns} FROM appointments WHERE starts_at >= @from AND starts_at < @to ORDER BY starts_at", conn);
        cmd.Parameters.AddWithValue("from", range.Start);
        cmd.Parameters.AddWithValue("to", range.End);
        return await ReadAllAsync(cmd, ct);
    }

    public static async Task<IReadOnlyList<Appointment>> BookedInRangeAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int practitionerId, TimeRange range, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"""
            SELECT {Columns} FROM appointments
            WHERE practitioner_id = @p AND status = 'booked' AND starts_at < @to AND blocked_until > @from
            ORDER BY starts_at
            """, conn, tx);
        cmd.Parameters.AddWithValue("p", practitionerId);
        cmd.Parameters.AddWithValue("from", range.Start);
        cmd.Parameters.AddWithValue("to", range.End);
        return await ReadAllAsync(cmd, ct);
    }

    /// <summary>The same check the slot list uses, run again inside the lock: the slot list the patient saw may be minutes old.</summary>
    private async Task<bool> StillFreeAsync(NpgsqlConnection conn, NpgsqlTransaction tx, ClinicService service, int practitionerId,
        DateTime startsAt, IReadOnlyList<WorkingWindow> hours, int? exceptId, CancellationToken ct)
    {
        var zone = _options.TimeZone();
        var day = ClinicClock.LocalDay(startsAt, zone);
        var busy = await ScheduleStore.BusyAsync(conn, tx, practitionerId, ClinicClock.DayRange(day, zone), exceptId, ct);
        var free = SlotCalculator.FreeSlots(day, new SlotService(service.DurationMinutes, service.BufferMinutes), hours, busy,
            _time.GetUtcNow().UtcDateTime, _options, zone);
        return free.Contains(startsAt);
    }

    private static void AddTimes(NpgsqlCommand cmd, ClinicService service, DateTime startsAt)
    {
        var end = startsAt.AddMinutes(service.DurationMinutes);
        cmd.Parameters.AddWithValue("start", startsAt);
        cmd.Parameters.AddWithValue("end", end);
        cmd.Parameters.AddWithValue("blocked", end.AddMinutes(service.BufferMinutes));
    }

    private static async Task<Appointment?> ReadOneAsync(NpgsqlCommand cmd, CancellationToken ct) =>
        (await ReadAllAsync(cmd, ct)).FirstOrDefault();

    private static async Task<IReadOnlyList<Appointment>> ReadAllAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<Appointment>();
        while (await r.ReadAsync(ct))
        {
            list.Add(new Appointment(
                r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.GetDateTime(7), r.GetDateTime(8), r.GetDateTime(9), r.GetString(10),
                r.IsDBNull(11) ? null : r.GetDateTime(11),
                r.IsDBNull(12) ? null : r.GetString(12),
                r.GetInt32(13), r.GetDateTime(14)));
        }
        return list;
    }
}
