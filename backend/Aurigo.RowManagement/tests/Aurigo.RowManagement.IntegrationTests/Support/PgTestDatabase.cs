using Npgsql;

namespace Aurigo.RowManagement.IntegrationTests.Support;

/// <summary>
/// A throwaway database on the real Postgres from docker-compose, so tests exercise the real migrations,
/// constraints, jsonb columns and transaction behaviour that EF InMemory cannot. One database per factory.
/// Override the server with ROW_TEST_PG ("Host=...;Port=...;Username=...;Password=...").
/// Set ROW_REQUIRE_PG=1 (CI, release gate) to make an unreachable Postgres FAIL tests instead of skipping them.
/// </summary>
public static class PgTestDatabase
{
    private static readonly string Server =
        Environment.GetEnvironmentVariable("ROW_TEST_PG") ?? "Host=127.0.0.1;Port=5434;Username=row;Password=row_dev_only";

    public static bool Required => Environment.GetEnvironmentVariable("ROW_REQUIRE_PG") == "1";

    private static readonly Lazy<string?> Unavailable = new(() =>
    {
        try
        {
            using var conn = new NpgsqlConnection(Server + ";Database=postgres;Timeout=3");
            conn.Open();
            return null;
        }
        catch (Exception ex)
        {
            return $"Postgres is not reachable ({ex.GetType().Name}). Run `docker compose up -d`, or set ROW_TEST_PG.";
        }
    });

    /// <summary>Why the real database cannot be used, or null when it can.</summary>
    public static string? UnavailableReason => Unavailable.Value;

    // Small pools that close quickly: the whole suite shares one Postgres (max 100 connections) and runs many apps at once.
    public static string ConnectionString(string database) =>
        $"{Server};Database={database};Include Error Detail=true;Maximum Pool Size=8;Connection Idle Lifetime=10;Connection Pruning Interval=5";

    public static async Task<string> CreateAsync()
    {
        var name = "row_it_" + Guid.NewGuid().ToString("N")[..16];
        await using var conn = new NpgsqlConnection(Server + ";Database=postgres");
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", conn);
        await cmd.ExecuteNonQueryAsync();
        return name;
    }

    public static async Task DropAsync(string name)
    {
        NpgsqlConnection.ClearAllPools(); // pooled connections would block the drop
        await using var conn = new NpgsqlConnection(Server + ";Database=postgres");
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Runs SQL directly against a test database (for asserting on real schema and data).</summary>
    public static async Task<List<object?[]>> QueryAsync(string database, string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new NpgsqlConnection(ConnectionString(database));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    public static async Task<long> ScalarAsync(string database, string sql, params (string Name, object Value)[] args) =>
        Convert.ToInt64((await QueryAsync(database, sql, args))[0][0]);
}

/// <summary>A [Fact] that needs the real Postgres. Skipped (with the reason) when it is down, unless ROW_REQUIRE_PG=1.</summary>
public sealed class PgFactAttribute : FactAttribute
{
    public PgFactAttribute()
    {
        if (!PgTestDatabase.Required && PgTestDatabase.UnavailableReason is { } reason) Skip = reason;
    }
}

/// <summary>A [Theory] that needs the real Postgres. See <see cref="PgFactAttribute"/>.</summary>
public sealed class PgTheoryAttribute : TheoryAttribute
{
    public PgTheoryAttribute()
    {
        if (!PgTestDatabase.Required && PgTestDatabase.UnavailableReason is { } reason) Skip = reason;
    }
}
