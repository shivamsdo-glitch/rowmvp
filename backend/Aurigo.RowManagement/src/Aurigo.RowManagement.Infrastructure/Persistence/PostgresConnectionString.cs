using System.Data.Common;

namespace Aurigo.RowManagement.Infrastructure.Persistence;

/// <summary>
/// Npgsql's default pool allows 100 connections, the same as Postgres' default max_connections. Under load,
/// or next to any other client of the same server, that means "too many clients" errors (HTTP 500) instead of
/// requests waiting their turn. Capping the pool makes the excess requests queue. An explicit
/// "Maximum Pool Size" in the connection string always wins.
/// </summary>
public static class PostgresConnectionString
{
    public const int DefaultMaxPoolSize = 40;

    public static string WithSafePool(string connectionString, int defaultMaxPoolSize = DefaultMaxPoolSize)
    {
        // The Npgsql builder reports every known keyword as present, so ask a plain builder what was actually written.
        var written = new DbConnectionStringBuilder { ConnectionString = connectionString };
        if (new[] { "Maximum Pool Size", "MaxPoolSize", "Max Pool Size" }.Any(written.ContainsKey))
            return connectionString;

        written["Maximum Pool Size"] = defaultMaxPoolSize;
        return written.ConnectionString;
    }
}
