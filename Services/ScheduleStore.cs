using Backend.Domain;
using Backend.Models;
using Npgsql;

namespace Backend.Services;

/// <summary>What is already taken: booked appointments (with their cleanup buffer) and time off.</summary>
public sealed class ScheduleStore
{
    private readonly Database _db;

    public ScheduleStore(Database db) => _db = db;

    /// <summary>Busy ranges for one practitioner that touch [range). exceptAppointmentId leaves out the appointment being
    /// moved, so a patient can move to a slot that overlaps their own current one.</summary>
    public static async Task<IReadOnlyList<TimeRange>> BusyAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, int practitionerId,
        TimeRange range, int? exceptAppointmentId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT starts_at, blocked_until FROM appointments
            WHERE practitioner_id = @p AND status = 'booked' AND starts_at < @to AND blocked_until > @from
              AND (@except IS NULL OR id <> @except)
            UNION ALL
            SELECT starts_at, ends_at FROM time_off
            WHERE practitioner_id = @p AND starts_at < @to AND ends_at > @from
            """, conn, tx);
        cmd.Parameters.AddWithValue("p", practitionerId);
        cmd.Parameters.AddWithValue("from", range.Start);
        cmd.Parameters.AddWithValue("to", range.End);
        cmd.Parameters.Add(new NpgsqlParameter<int?>("except", exceptAppointmentId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var busy = new List<TimeRange>();
        while (await reader.ReadAsync(ct)) busy.Add(new TimeRange(reader.GetDateTime(0), reader.GetDateTime(1)));
        return busy;
    }

    public async Task<IReadOnlyList<TimeRange>> BusyAsync(int practitionerId, TimeRange range, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        return await BusyAsync(conn, null, practitionerId, range, null, ct);
    }

    public async Task<IReadOnlyList<TimeOff>> TimeOffAsync(TimeRange range, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT id, practitioner_id, starts_at, ends_at, reason FROM time_off WHERE starts_at < @to AND ends_at > @from ORDER BY starts_at", conn);
        cmd.Parameters.AddWithValue("from", range.Start);
        cmd.Parameters.AddWithValue("to", range.End);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<TimeOff>();
        while (await reader.ReadAsync(ct))
            list.Add(new TimeOff(reader.GetInt32(0), reader.GetInt32(1), reader.GetDateTime(2), reader.GetDateTime(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        return list;
    }

    /// <summary>
    /// Adds time off unless it clashes with booked appointments. Clashes are returned, not cancelled: staff call those
    /// patients first, then cancel them from the day view, then add the time off.
    /// </summary>
    public async Task<(TimeOff? Added, IReadOnlyList<Appointment> Clashes)> AddTimeOffAsync(int practitionerId, TimeRange range, string? reason, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await AppointmentStore.LockPractitionerAsync(conn, tx, practitionerId, ct);

        var clashes = await AppointmentStore.BookedInRangeAsync(conn, tx, practitionerId, range, ct);
        if (clashes.Count > 0) return (null, clashes);

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO time_off (practitioner_id, starts_at, ends_at, reason) VALUES (@p, @from, @to, @reason) RETURNING id", conn, tx);
        cmd.Parameters.AddWithValue("p", practitionerId);
        cmd.Parameters.AddWithValue("from", range.Start);
        cmd.Parameters.AddWithValue("to", range.End);
        cmd.Parameters.Add(new NpgsqlParameter<string?>("reason", reason));
        var id = (int)(await cmd.ExecuteScalarAsync(ct))!;
        await tx.CommitAsync(ct);
        return (new TimeOff(id, practitionerId, range.Start, range.End, reason), Array.Empty<Appointment>());
    }

    public async Task<bool> DeleteTimeOffAsync(int id, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("DELETE FROM time_off WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }
}
