using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
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
/// Sample projects for demos: one federal project still held at the acquisition gate, one federal project
/// cleared to acquire (NEPA + acquisition authorization, two estimate versions, two budget years) and one
/// locally funded project. Idempotent: skipped when the first demo project already exists. Parcels join in slice 2.
/// </summary>
public sealed class DemoDataSeedStep : ISeedStep
{
    private readonly RowDbContext _db;
    private readonly ILogger<DemoDataSeedStep> _log;

    public DemoDataSeedStep(RowDbContext db, ILogger<DemoDataSeedStep> log)
    {
        _db = db;
        _log = log;
    }

    public string Name => "Demo projects and parcels";
    public int Order => 40;
    public bool DevelopmentOnly => true;

    public async Task RunAsync(CancellationToken ct)
    {
        _db.CurrentTenantId = WellKnownIds.DevTenant;
        if (await _db.Projects.AnyAsync(p => p.Number == "DEMO-001", ct)) return;

        var held = new Project
        {
            Number = "DEMO-001", Name = "SR-20 Corridor Widening", Region = "North", District = "District 1",
            Description = "Federal-aid widening; NEPA decision and acquisition authorization still pending.",
            OversightType = OversightType.Federal, FundingSource = "FHWA STBG", FundingAmount = 12_000_000m, DesignEstimate = 9_500_000m,
            LettingDate = new DateOnly(2027, 6, 1), ConstructionStartDate = new DateOnly(2027, 9, 1), ConstructionEndDate = new DateOnly(2029, 3, 31),
        };
        held.Authorizations.Add(new RowAuthorization
        {
            Type = AuthorizationType.Preliminary, AuthorizedAmount = 400_000m, FederalSharePercent = 80m,
            AuthorizedOn = new DateOnly(2026, 1, 20), ReferenceNumber = "PRE-0001", ProgrammingConfirmed = true,
            Notes = "Appraisal and title only.",
        });

        var cleared = new Project
        {
            Number = "DEMO-002", Name = "Harbor Bridge Replacement", Region = "South", District = "District 3",
            Description = "Federal-aid bridge replacement cleared to acquire.",
            OversightType = OversightType.Federal, FundingSource = "FHWA Bridge Program", FundingAmount = 30_000_000m, DesignEstimate = 24_000_000m,
            LettingDate = new DateOnly(2027, 3, 1), NepaDecisionType = NepaDecisionType.CategoricalExclusion, NepaDecisionDate = new DateOnly(2025, 11, 12),
        };
        cleared.Authorizations.Add(new RowAuthorization
        {
            Type = AuthorizationType.Acquisition, AuthorizedAmount = 6_500_000m, FederalSharePercent = 80m,
            AuthorizedOn = new DateOnly(2026, 2, 10), ReferenceNumber = "ACQ-0007", ProgrammingConfirmed = true,
        });
        cleared.Estimates.Add(new ProjectCostEstimate
        {
            Version = 1, LandCost = 4_000_000m, ImprovementsCost = 900_000m, RelocationCost = 600_000m, IndirectCost = 350_000m,
            Notes = "Preliminary quantity-based estimate.",
        });
        cleared.Estimates.Add(new ProjectCostEstimate
        {
            Version = 2, LandCost = 4_400_000m, ImprovementsCost = 950_000m, RelocationCost = 650_000m, IndirectCost = 400_000m,
            Notes = "Revised after parcel list update.",
        });
        foreach (var e in cleared.Estimates) e.RecalculateTotal();
        cleared.Budgets.Add(new ProjectBudget { FiscalYear = 2026, Amount = 2_500_000m });
        cleared.Budgets.Add(new ProjectBudget { FiscalYear = 2027, Amount = 4_000_000m, Notes = "Peak acquisition year." });

        var local = new Project
        {
            Number = "DEMO-003", Name = "Maple Avenue Sidewalk Extension", Region = "Central", District = "District 2",
            Description = "Locally funded; agency rules apply, so the acquisition gate does not hold it.",
            OversightType = OversightType.Local, FundingSource = "City capital fund", FundingAmount = 850_000m, DesignEstimate = 700_000m,
        };

        _db.Projects.AddRange(held, cleared, local);
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Seeded 3 demo projects.");
    }
}
