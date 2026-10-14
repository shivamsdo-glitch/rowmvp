using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Seed;

/// <summary>The one demo tenant the MVP runs on (dev tokens default to it).</summary>
public sealed class DemoTenantSeedStep : ISeedStep
{
    private readonly RowDbContext _db;
    public DemoTenantSeedStep(RowDbContext db) => _db = db;

    public string Name => "Demo tenant";
    public int Order => 10;
    public bool DevelopmentOnly => true;

    public async Task RunAsync(CancellationToken ct)
    {
        if (await _db.Tenants.AnyAsync(t => t.Id == WellKnownIds.DevTenant, ct)) return;

        _db.Tenants.Add(new Tenant
        {
            Id = WellKnownIds.DevTenant,
            Name = "Demo Agency (Development)",
            Slug = "demo",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Default roles and page permissions for every active tenant. Runs in every environment: a tenant that
/// exists but lacks them (for example after a release adds a page) is brought up to date. Never overwrites edits.
/// </summary>
public sealed class DefaultRbacSeedStep : ISeedStep
{
    private readonly RowDbContext _db;
    private readonly TenantProvisioner _provisioner;
    private readonly ILogger<DefaultRbacSeedStep> _log;

    public DefaultRbacSeedStep(RowDbContext db, TenantProvisioner provisioner, ILogger<DefaultRbacSeedStep> log)
    {
        _db = db;
        _provisioner = provisioner;
        _log = log;
    }

    public string Name => "Default roles and page permissions";
    public int Order => 20;
    public bool DevelopmentOnly => false;

    public async Task RunAsync(CancellationToken ct)
    {
        var tenantIds = await _db.Tenants.Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct);
        foreach (var tenantId in tenantIds)
        {
            var added = await _provisioner.EnsureDefaultRolesAsync(tenantId, ct);
            if (added > 0) _log.LogInformation("Tenant {TenantId}: added {Count} default role/permission rows.", tenantId, added);
        }
    }
}

/// <summary>
/// One not-yet-activated user per role (<c>projectmanager@row.local</c> ...) in the demo tenant.
/// To sign in as one, POST /api/v1/auth/forgot-password for the address and follow the activation link
/// written to the console, or use GET /api/v1/auth/dev-token.
/// </summary>
public sealed class DemoUsersSeedStep : ISeedStep
{
    private readonly RowDbContext _db;
    public DemoUsersSeedStep(RowDbContext db) => _db = db;

    public string Name => "Demo users";
    public int Order => 30;
    public bool DevelopmentOnly => true;

    public async Task RunAsync(CancellationToken ct)
    {
        var tenantId = WellKnownIds.DevTenant;
        var roles = await _db.Roles.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && RoleNames.All.Contains(r.Name))
            .ToListAsync(ct);

        foreach (var role in roles)
        {
            var email = $"{role.Name.ToLowerInvariant()}@row.local";
            if (await _db.AppUsers.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct)) continue;

            var user = new AppUser { TenantId = tenantId, FirstName = role.Name, LastName = "Demo", Email = email, IsActive = true };
            user.UserRoles.Add(new UserRole { TenantId = tenantId, UserId = user.Id, RoleId = role.Id });
            _db.AppUsers.Add(user);
        }
        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Placeholder for the sample projects and parcels used in demos. The entities arrive in Phase 1, so
/// this only logs for now; when they exist, add the (idempotent) inserts here and nothing else changes.
/// </summary>
public sealed class DemoDataSeedStep : ISeedStep
{
    private readonly ILogger<DemoDataSeedStep> _log;
    public DemoDataSeedStep(ILogger<DemoDataSeedStep> log) => _log = log;

    public string Name => "Demo projects and parcels";
    public int Order => 40;
    public bool DevelopmentOnly => true;

    public Task RunAsync(CancellationToken ct)
    {
        _log.LogInformation("Demo projects and parcels: nothing to seed yet (project and parcel entities arrive in Phase 1).");
        return Task.CompletedTask;
    }
}
