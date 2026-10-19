using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Email;
using Aurigo.RowManagement.IntegrationTests.Support;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>Records every email instead of sending it, so tests can follow activation and reset links.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();
    public IReadOnlyList<EmailMessage> Sent => _sent.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>The raw single-use token from the most recent email to the address.</summary>
    public string LastTokenFor(string address)
    {
        var mail = _sent.Last(m => m.ToAddress == address);
        return Regex.Match(mail.TextBody, @"token=([A-Za-z0-9_-]+)").Groups[1].Value;
    }
}

/// <summary>
/// Hosts the real API (real middleware, auth, rate limiting, MediatR, audit interceptor) over an
/// in-memory database, so no Postgres is needed. One fresh database per factory instance.
/// </summary>
public sealed class RowApiFactory : WebApplicationFactory<Program>
{
    private readonly string _environment;
    private readonly Dictionary<string, string?> _settings;
    private readonly string _dbName = Guid.NewGuid().ToString("N");
    private string? _pgDatabase;      // set => run against the real Postgres instead of EF InMemory
    private bool _ownsPgDatabase;

    /// <summary>The real Postgres database in use, or null when running on EF InMemory.</summary>
    public string? PgDatabase => _pgDatabase;

    /// <summary>
    /// Runs the real API over a real, freshly migrated Postgres database (created now, dropped on dispose).
    /// Pass <paramref name="existingDatabase"/> to boot a second app instance over the same data (restart tests).
    /// </summary>
    public static RowApiFactory WithPostgres(Dictionary<string, string?>? settings = null, string? existingDatabase = null, string environment = "Development")
    {
        var factory = new RowApiFactory(environment, settings);
        factory._pgDatabase = existingDatabase ?? PgTestDatabase.CreateAsync().GetAwaiter().GetResult();
        factory._ownsPgDatabase = existingDatabase is null;
        return factory;
    }

    public CapturingEmailSender Emails { get; } = new();

    /// <summary>Required by xUnit class fixtures.</summary>
    public RowApiFactory() : this("Development", null) { }

    /// <summary>A Development factory with extra configuration, e.g. tight rate limits.</summary>
    public static RowApiFactory WithSettings(Dictionary<string, string?> settings) => new("Development", settings);

    internal RowApiFactory(string environment, Dictionary<string, string?>? settings = null)
    {
        _environment = environment;
        _settings = new Dictionary<string, string?>
        {
            // Generous by default so unrelated tests never trip the limiters; rate-limit tests override.
            ["RateLimiting:AuthPermitLimit"] = "1000",
            ["RateLimiting:WritePermitLimit"] = "1000",
        };
        if (settings is not null)
            foreach (var kv in settings) _settings[kv.Key] = kv.Value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        if (_pgDatabase is not null)
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = PgTestDatabase.ConnectionString(_pgDatabase),
            }));
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(_settings));
        builder.ConfigureServices(services =>
        {
            if (_pgDatabase is null)
            {
                services.Remove(services.Single(d => d.ServiceType == typeof(DbContextOptions<RowDbContext>)));
                services.AddDbContext<RowDbContext>((sp, opts) => opts
                    .UseInMemoryDatabase(_dbName)
                    .AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>()));
            }

            services.Remove(services.Single(d => d.ServiceType == typeof(IEmailSender)));
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        try { AddNotYetActivatedUsers(host.Services); }
        catch (Exception) when (_settings.TryGetValue("Database:SeedOnStartup", out var seed) && seed == "false") { /* a test that boots the app without a reachable or seeded database */ }
        return host;
    }

    /// <summary>
    /// One invited-but-never-signed-in user per role (<c>negotiator@row.local</c> ...), the fixture the account-flow tests
    /// (invite, activate, reset) run against. The product's own demo seed signs in eleven named users instead.
    /// </summary>
    private static void AddNotYetActivatedUsers(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();
        var tenantId = Aurigo.RowManagement.Domain.Entities.WellKnownIds.DevTenant;
        var roles = db.Roles.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).ToList();
        foreach (var role in roles)
        {
            var email = $"{role.Name.ToLowerInvariant()}@row.local";
            if (db.AppUsers.IgnoreQueryFilters().Any(u => u.Email == email)) continue;
            var user = new Aurigo.RowManagement.Domain.Entities.AppUser { TenantId = tenantId, FirstName = role.Name, LastName = "Demo", Email = email, IsActive = true };
            user.UserRoles.Add(new Aurigo.RowManagement.Domain.Entities.UserRole { TenantId = tenantId, UserId = user.Id, RoleId = role.Id });
            db.AppUsers.Add(user);
        }
        db.SaveChanges();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_pgDatabase is not null && _ownsPgDatabase) await PgTestDatabase.DropAsync(_pgDatabase);
    }

    public async Task<string> DevTokenAsync(HttpClient client, string role, Guid? tenant = null, Guid? user = null)
    {
        var url = $"/api/v1/auth/dev-token?role={role}";
        if (tenant is not null) url += $"&tenant={tenant}";
        if (user is not null) url += $"&user={user}";
        var response = await client.GetFromJsonAsync<DevTokenResponse>(url, JsonOptions);
        return response!.Token;
    }

    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);
}
