using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Email;
using Aurigo.RowManagement.Application.ExternalServices;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Infrastructure.Auth;
using Aurigo.RowManagement.Infrastructure.Email;
using Aurigo.RowManagement.Infrastructure.ExternalClients;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Interceptors;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aurigo.RowManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        // ── Tenancy and identity ───────────────────────────────────────────
        services.AddScoped<ICurrentTenant, CurrentTenantAccessor>();
        services.AddScoped<ICurrentUser, CurrentUserAccessor>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<ITokenService, OneTimeTokenService>();

        // Resolved lazily so configuration supplied late (tests, secret stores) is honoured.
        services.AddSingleton(sp =>
            JwtOptions.Resolve(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>()));
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // ── Email: console stub only. A real provider would replace this one registration. ──
        services.AddSingleton<IEmailSender, ConsoleEmailSender>();
        services.AddScoped<IAccountMailer, AccountMailer>();

        // ── Persistence ────────────────────────────────────────────────────
        services.AddScoped<AuditingSaveChangesInterceptor>();
        services.AddDbContext<RowDbContext>((sp, opts) =>
        {
            var connStr = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
                          ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
            opts.UseNpgsql(PostgresConnectionString.WithSafePool(connStr), npg => npg.MigrationsHistoryTable("__ef_migrations_history"))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>());
        });
        services.AddScoped<IRowDbContext>(sp => sp.GetRequiredService<RowDbContext>());

        // ── Seeding: SeedRunner runs the steps in order after migrations ──
        services.AddScoped<TenantProvisioner>();
        services.AddScoped<ISeedStep, DemoTenantSeedStep>();
        services.AddScoped<ISeedStep, DefaultRbacSeedStep>();
        services.AddScoped<ISeedStep, DemoUsersSeedStep>();
        services.AddScoped<ISeedStep, DemoDataSeedStep>();
        services.AddScoped<SeedRunner>();

        // ── External integrations: stub-first ──────────────────────────────
        // Nothing outside this process is called in the MVP. To integrate a real system, add a real
        // client next to the stub and swap the registration; callers depend only on the interface
        // in Application/ExternalServices.
        services.AddOptions<FileStorageOptions>().Configure<IConfiguration>((o, c) => c.GetSection(FileStorageOptions.Section).Bind(o));
        services.AddSingleton<Aurigo.RowManagement.Application.Storage.IFileStorage, LocalDiskFileStorage>();
        services.AddSingleton<IDocMgmtClient, StubDocMgmtClient>();
        services.AddSingleton<INotificationClient, StubNotificationClient>();

        return services;
    }
}
