using Backend.Domain;
using Backend.Models;
using Npgsql;

namespace Backend.Services;

/// <summary>Services, practitioners and working hours: what the clinic offers and when.</summary>
public sealed class CatalogStore
{
    private readonly Database _db;

    public CatalogStore(Database db) => _db = db;

    public async Task<IReadOnlyList<ClinicService>> ServicesAsync(CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT id, name, description, duration_minutes, buffer_minutes, price_cents FROM services WHERE active ORDER BY id", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<ClinicService>();
        while (await reader.ReadAsync(ct)) list.Add(ReadService(reader));
        return list;
    }

    public async Task<ClinicService?> ServiceAsync(int id, CancellationToken ct) =>
        (await ServicesAsync(ct)).FirstOrDefault(s => s.Id == id);

    /// <summary>Active practitioners with the services each one offers; serviceId narrows the list to one service.</summary>
    public async Task<IReadOnlyList<Practitioner>> PractitionersAsync(int? serviceId, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT p.id, p.name, p.title, p.bio, coalesce(array_agg(ps.service_id ORDER BY ps.service_id) FILTER (WHERE ps.service_id IS NOT NULL), '{}')
            FROM practitioners p
            LEFT JOIN practitioner_services ps ON ps.practitioner_id = p.id
            WHERE p.active
            GROUP BY p.id
            HAVING @service_id IS NULL OR bool_or(ps.service_id = @service_id)
            ORDER BY p.id
            """, conn);
        cmd.Parameters.Add(new NpgsqlParameter<int?>("service_id", serviceId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<Practitioner>();
        while (await reader.ReadAsync(ct))
            list.Add(new Practitioner(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<int[]>(4)));
        return list;
    }

    public async Task<Practitioner?> PractitionerAsync(int id, CancellationToken ct) =>
        (await PractitionersAsync(null, ct)).FirstOrDefault(p => p.Id == id);

    /// <summary>The service and practitioner for a booking, if this practitioner offers this service. Error codes:
    /// no_service / no_practitioner (404), not_offered (400).</summary>
    public async Task<(ClinicService? Service, Practitioner? Practitioner, ApiError? Error)> OfferAsync(int serviceId, int practitionerId, CancellationToken ct)
    {
        var service = await ServiceAsync(serviceId, ct);
        if (service is null) return (null, null, new ApiError("no_service", "That service does not exist."));
        var practitioner = await PractitionerAsync(practitionerId, ct);
        if (practitioner is null) return (null, null, new ApiError("no_practitioner", "That practitioner does not exist."));
        if (!practitioner.ServiceIds.Contains(serviceId))
            return (null, null, new ApiError("not_offered", $"{practitioner.Name} does not offer {service.Name}."));
        return (service, practitioner, null);
    }

    public async Task<IReadOnlyList<WorkingWindow>> WorkingHoursAsync(int practitionerId, CancellationToken ct, NpgsqlConnection? conn = null)
    {
        var own = conn is null;
        conn ??= await _db.OpenAsync(ct);
        try
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT day_of_week, start_time, end_time FROM working_hours WHERE practitioner_id = @id ORDER BY day_of_week, start_time", conn);
            cmd.Parameters.AddWithValue("id", practitionerId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var list = new List<WorkingWindow>();
            while (await reader.ReadAsync(ct))
                list.Add(new WorkingWindow((DayOfWeek)reader.GetInt16(0), TimeOnly.FromTimeSpan(reader.GetTimeSpan(1)), TimeOnly.FromTimeSpan(reader.GetTimeSpan(2))));
            return list;
        }
        finally
        {
            if (own) await conn.DisposeAsync();
        }
    }

    private static ClinicService ReadService(NpgsqlDataReader r) =>
        new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5));
}
