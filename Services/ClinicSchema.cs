using Npgsql;

namespace Backend.Services;

/// <summary>
/// Creates the clinic tables on startup (every statement is safe to run again) and adds the sample clinic once, when
/// there are no practitioners yet. All times are timestamptz in UTC; working hours are clinic-local "time" values.
/// </summary>
public static class ClinicSchema
{
    private const string Tables = """
        CREATE TABLE IF NOT EXISTS practitioners (
            id      serial PRIMARY KEY,
            name    text NOT NULL,
            title   text NOT NULL,
            bio     text NOT NULL DEFAULT '',
            active  boolean NOT NULL DEFAULT true
        );

        CREATE TABLE IF NOT EXISTS services (
            id                serial PRIMARY KEY,
            name              text NOT NULL UNIQUE,
            description       text NOT NULL DEFAULT '',
            duration_minutes  integer NOT NULL CHECK (duration_minutes BETWEEN 5 AND 240),
            buffer_minutes    integer NOT NULL DEFAULT 0 CHECK (buffer_minutes BETWEEN 0 AND 60),
            price_cents       integer NOT NULL CHECK (price_cents >= 0),
            active            boolean NOT NULL DEFAULT true
        );

        CREATE TABLE IF NOT EXISTS practitioner_services (
            practitioner_id  integer NOT NULL REFERENCES practitioners(id) ON DELETE CASCADE,
            service_id       integer NOT NULL REFERENCES services(id) ON DELETE CASCADE,
            PRIMARY KEY (practitioner_id, service_id)
        );

        CREATE TABLE IF NOT EXISTS working_hours (
            id               serial PRIMARY KEY,
            practitioner_id  integer NOT NULL REFERENCES practitioners(id) ON DELETE CASCADE,
            day_of_week      smallint NOT NULL CHECK (day_of_week BETWEEN 0 AND 6),
            start_time       time NOT NULL,
            end_time         time NOT NULL,
            CHECK (end_time > start_time)
        );

        CREATE TABLE IF NOT EXISTS time_off (
            id               serial PRIMARY KEY,
            practitioner_id  integer NOT NULL REFERENCES practitioners(id) ON DELETE CASCADE,
            starts_at        timestamptz NOT NULL,
            ends_at          timestamptz NOT NULL,
            reason           text,
            CHECK (ends_at > starts_at)
        );

        CREATE TABLE IF NOT EXISTS appointments (
            id                 serial PRIMARY KEY,
            practitioner_id    integer NOT NULL REFERENCES practitioners(id),
            service_id         integer NOT NULL REFERENCES services(id),
            patient_name       text NOT NULL,
            patient_email      text NOT NULL,
            patient_phone      text NOT NULL,
            notes              text,
            starts_at          timestamptz NOT NULL,
            ends_at            timestamptz NOT NULL,
            blocked_until      timestamptz NOT NULL,
            status             text NOT NULL DEFAULT 'booked' CHECK (status IN ('booked', 'cancelled', 'completed', 'no_show')),
            manage_token_hash  char(64) NOT NULL UNIQUE,
            cancelled_at       timestamptz,
            cancel_reason      text,
            reschedule_count   integer NOT NULL DEFAULT 0,
            created_at         timestamptz NOT NULL DEFAULT now(),
            CHECK (ends_at > starts_at AND blocked_until >= ends_at)
        );

        CREATE INDEX IF NOT EXISTS appointments_by_practitioner_time ON appointments (practitioner_id, starts_at);
        CREATE INDEX IF NOT EXISTS time_off_by_practitioner_time ON time_off (practitioner_id, starts_at);
        """;

    public static async Task EnsureAsync(Database db, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using (var create = new NpgsqlCommand(Tables, conn))
            await create.ExecuteNonQueryAsync(ct);

        await using var count = new NpgsqlCommand("SELECT count(*) FROM practitioners", conn);
        if ((long)(await count.ExecuteScalarAsync(ct))! > 0) return;

        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var seed = new NpgsqlCommand(SampleClinic, conn, tx))
            await seed.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    // Sunday to Thursday is the working week (day_of_week: 0 = Sunday). Dr. Levi has a split day on Tuesday.
    private const string SampleClinic = """
        INSERT INTO services (name, description, duration_minutes, buffer_minutes, price_cents) VALUES
            ('General consultation', 'A visit with a family doctor for a new or ongoing problem.', 20, 5, 0),
            ('Annual check-up', 'A full yearly check, including blood pressure and a review of your medication.', 40, 10, 0),
            ('Physiotherapy session', 'Assessment and treatment for back, neck and joint pain.', 45, 15, 18000),
            ('Vaccination', 'Flu, travel and routine vaccines given by a nurse.', 10, 5, 0);

        INSERT INTO practitioners (name, title, bio) VALUES
            ('Dr. Noa Levi', 'Family doctor', 'Fifteen years in family medicine, with a focus on preventive care.'),
            ('Dr. Daniel Cohen', 'Family doctor', 'Sees adults and teenagers; speaks English, Hebrew and French.'),
            ('Maya Rosen', 'Physiotherapist', 'Sports injuries and back pain; trained in manual therapy.'),
            ('Yael Katz', 'Practice nurse', 'Vaccinations, blood tests and travel health advice.');

        INSERT INTO practitioner_services (practitioner_id, service_id)
        SELECT p.id, s.id FROM practitioners p JOIN services s ON
            (p.name IN ('Dr. Noa Levi', 'Dr. Daniel Cohen') AND s.name IN ('General consultation', 'Annual check-up'))
            OR (p.name = 'Maya Rosen' AND s.name = 'Physiotherapy session')
            OR (p.name = 'Yael Katz' AND s.name = 'Vaccination');

        INSERT INTO working_hours (practitioner_id, day_of_week, start_time, end_time)
        SELECT p.id, d, w.start_time, w.end_time
        FROM practitioners p
        CROSS JOIN generate_series(0, 4) AS d
        JOIN (VALUES
            ('Dr. Noa Levi', '08:00'::time, '13:00'::time),
            ('Dr. Daniel Cohen', '12:00'::time, '19:00'::time),
            ('Maya Rosen', '09:00'::time, '17:00'::time),
            ('Yael Katz', '08:00'::time, '12:00'::time)
        ) AS w(name, start_time, end_time) ON w.name = p.name
        WHERE NOT (p.name = 'Dr. Noa Levi' AND d = 2);

        INSERT INTO working_hours (practitioner_id, day_of_week, start_time, end_time)
        SELECT id, 2, t.start_time, t.end_time FROM practitioners
        CROSS JOIN (VALUES ('08:00'::time, '11:00'::time), ('15:00'::time, '19:00'::time)) AS t(start_time, end_time)
        WHERE name = 'Dr. Noa Levi';
        """;
}
