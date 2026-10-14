using Aurigo.RowManagement.Application.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aurigo.RowManagement.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` to create migrations; never at runtime and never connects to a database.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RowDbContext>
{
    public RowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RowDbContext>()
            .UseNpgsql("Host=localhost;Database=row_management_design_time", npg =>
                npg.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new RowDbContext(options, new NoTenant());
    }

    private sealed class NoTenant : ICurrentTenant
    {
        public Guid Value => Guid.Empty;
        public bool IsAvailable => false;
    }
}
