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
}
