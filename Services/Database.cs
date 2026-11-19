using Npgsql;

namespace Backend.Services;

/// <summary>Opens database connections. The app's database role has a restricted default search_path, so public is set first.</summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Database connection string not found");
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var setPath = new NpgsqlCommand("SET search_path = public, \"$user\";", conn);
        await setPath.ExecuteNonQueryAsync(ct);
        return conn;
    }
}
