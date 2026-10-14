using Aurigo.RowManagement.Infrastructure.Persistence;
using FluentAssertions;
using Npgsql;

namespace Aurigo.RowManagement.UnitTests.Persistence;

public class PostgresConnectionStringTests
{
    private const string Base = "Host=db;Port=5432;Username=u;Password=p;Database=d";

    [Fact]
    public void Without_an_explicit_pool_size_the_pool_is_capped_below_the_servers_default_connection_limit()
    {
        var result = new NpgsqlConnectionStringBuilder(PostgresConnectionString.WithSafePool(Base));

        result.MaxPoolSize.Should().Be(PostgresConnectionString.DefaultMaxPoolSize);
        result.MaxPoolSize.Should().BeLessThan(100, "Postgres defaults to 100 connections in total");
    }

    [Fact]
    public void An_explicit_pool_size_is_respected()
    {
        var result = new NpgsqlConnectionStringBuilder(PostgresConnectionString.WithSafePool(Base + ";Maximum Pool Size=7"));

        result.MaxPoolSize.Should().Be(7);
    }

    [Fact]
    public void Everything_else_in_the_connection_string_is_preserved()
    {
        var result = new NpgsqlConnectionStringBuilder(PostgresConnectionString.WithSafePool(Base));

        (result.Host, result.Port, result.Username, result.Password, result.Database).Should().Be(("db", 5432, "u", "p", "d"));
    }
}
