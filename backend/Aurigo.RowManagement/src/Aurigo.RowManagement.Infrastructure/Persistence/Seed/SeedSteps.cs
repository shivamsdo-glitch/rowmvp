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
        if (!await _db.Projects.AnyAsync(p => p.Number == "DEMO-001", ct)) await SeedProjectsAsync(ct);
        await SeedParcelsAsync(ct);
        await SeedPartiesAsync(ct);
        await SeedParcelRecordsAsync(ct);
        await SeedAcquisitionRecordsAsync(ct);
    }

    private async Task SeedProjectsAsync(CancellationToken ct)
    {

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

    /// <summary>
    /// Sample parcels: a bridge corridor with a parent tract, a parcel at the offer step, one whose offer deadline has run out
    /// (so reminders have something to show), a split-and-remnant pair, and a closed parcel. Skipped once the project has parcels.
    /// </summary>
    private async Task SeedParcelsAsync(CancellationToken ct)
    {
        var bridge = await _db.Projects.FirstOrDefaultAsync(p => p.Number == "DEMO-002", ct);
        var corridorProject = await _db.Projects.FirstOrDefaultAsync(p => p.Number == "DEMO-001", ct);
        if (bridge is null || corridorProject is null) return;
        if (await _db.Parcels.AnyAsync(p => p.ProjectId == bridge.Id || p.ProjectId == corridorProject.Id, ct)) return;

        var statuses = await _db.ParcelStatuses.ToDictionaryAsync(s => s.Name, s => s.Id, ct);
        var templates = await _db.WorkflowTemplates.Include(t => t.Steps).ToDictionaryAsync(t => t.AcquisitionType, ct);
        if (statuses.Count == 0 || templates.Count == 0) return; // rules are provisioned by the RBAC step; nothing to seed against yet

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var span = new ParcelGroup { ProjectId = bridge.Id, Name = "Main span", Description = "Parcels along the bridge approach" };
        var tract = new ParentTract { ProjectId = bridge.Id, Number = "T-1001", Description = "Harbor Holdings LLC", TaxParcelNumbers = new List<string> { "APN 044-120-07", "APN 044-120-08" } };
        _db.ParcelGroups.Add(span);
        _db.ParentTracts.Add(tract);

        Parcel Make(Project project, string number, string status, string step, decimal area, string? address, ParcelGroup? group = null, ParentTract? parent = null, bool remnant = false)
        {
            var parcel = new Parcel
            {
                ProjectId = project.Id, Number = number, Address = address, AreaSqFt = area, PropertyType = "Commercial",
                StatusId = statuses[status], CurrentStepKey = step, GroupId = group?.Id, ParentTractId = parent?.Id, IsRemnant = remnant,
            };
            foreach (var name in MilestoneNames.Standard)
                parcel.Milestones.Add(new ParcelMilestone { ParcelId = parcel.Id, Name = name, CompletedOn = name == MilestoneNames.ParcelSetUp ? today.AddDays(-200) : null });
            return parcel;
        }
        void Done(Parcel p, string milestone, DateOnly on) => p.Milestones.Single(m => m.Name == milestone).CompletedOn = on;

        var atOffer = Make(bridge, "001", "Initial Offer", "offer", 12_500m, "100 Harbor Rd", span, tract);
        Done(atOffer, MilestoneNames.NoticeToOwner, today.AddDays(-30));
        atOffer.NoticeIssuedOn = today.AddDays(-30);
        atOffer.NoticeMethod = NoticeMethod.CertifiedMail;
        atOffer.Interests.Add(new ParcelInterest { ParcelId = atOffer.Id, Type = InterestType.FeeTaking, AreaSqFt = 8_000m });
        atOffer.Interests.Add(new ParcelInterest { ParcelId = atOffer.Id, Type = InterestType.TemporaryEasement, AreaSqFt = 2_000m, Notes = "Staging area, 18 months" });
        atOffer.SiteVisits.Add(new ParcelSiteVisit { ParcelId = atOffer.Id, VisitedOn = today.AddDays(-45), Notes = "Walked the frontage with the owner's representative.", OwnerContactConfirmed = true });

        var late = Make(bridge, "002", "Initial Offer", "negotiation", 6_200m, "112 Harbor Rd", span, tract);
        Done(late, MilestoneNames.NoticeToOwner, today.AddDays(-120)); // the 90-day offer deadline has passed
        late.NoticeIssuedOn = today.AddDays(-120);
        late.NoticeMethod = NoticeMethod.HandDelivery;

        var closed = Make(bridge, "003", "In Possession - CLOSED", "closing", 3_000m, "9 Pier Ln", span);
        Done(closed, MilestoneNames.NoticeToOwner, today.AddDays(-190));
        Done(closed, MilestoneNames.OfferDelivered, today.AddDays(-150));
        Done(closed, MilestoneNames.AgreementSigned, today.AddDays(-100));
        Done(closed, MilestoneNames.Closed, today.AddDays(-60));

        var original = Make(bridge, "004", "Pre-Acquisition", "setup", 20_000m, "Vacant lot, Pier Ln");
        original.State = ParcelState.Split;
        original.SupersededOn = DateTime.UtcNow;
        var partA = Make(bridge, "004-A", "Pre-Acquisition", "setup", 12_000m, "Vacant lot, Pier Ln");
        var remnant = Make(bridge, "004-R", "Pre-Acquisition", "setup", 8_000m, "Vacant lot, Pier Ln", remnant: true);
        partA.SourceParcelIds = new List<Guid> { original.Id };
        remnant.SourceParcelIds = new List<Guid> { original.Id };

        var held1 = Make(corridorProject, "001", "Pre-Acquisition", "setup", 15_000m, "2 State Route 20");
        var held2 = Make(corridorProject, "002", "In Design", "title_survey", 9_800m, "8 State Route 20");

        _db.Parcels.AddRange(atOffer, late, closed, original, partA, remnant, held1, held2);
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Seeded demo parcels.");
    }

    /// <summary>A team, a diary and a note on the demo parcel at the offer step. Skipped once any exist.</summary>
    private async Task SeedParcelRecordsAsync(CancellationToken ct)
    {
        if (await _db.ParcelTeamMembers.AnyAsync(ct)) return;
        var bridgeId = await _db.Projects.Where(p => p.Number == "DEMO-002").Select(p => p.Id).FirstOrDefaultAsync(ct);
        var parcel = await _db.Parcels.FirstOrDefaultAsync(p => p.Number == "001" && p.ProjectId == bridgeId, ct);
        if (parcel is null) return;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        ParcelTeamMember Member(TeamRole role, string name, string? org, bool consultant) =>
            new() { ParcelId = parcel.Id, Role = role, Name = name, NameKey = name.ToUpperInvariant(), Organization = org, IsConsultant = consultant };
        _db.ParcelTeamMembers.AddRange(
            Member(TeamRole.ProjectLead, "Dana Whitfield", "Agency ROW office", false),
            Member(TeamRole.Negotiator, "Riley Okafor", "Okafor Land Services", true),
            Member(TeamRole.Appraiser, "Allison Jackson", "CBRE, Inc.", true));
        _db.ParcelDiaryEntries.AddRange(
            new ParcelDiaryEntry { ParcelId = parcel.Id, OccurredOn = today.AddDays(-28), Method = DiaryMethod.Call, Direction = DiaryDirection.Outgoing, ContactName = "Alex Marlowe", Summary = "Introduced the project and explained the offer process.", Outcome = "Asked for the plans by email." },
            new ParcelDiaryEntry { ParcelId = parcel.Id, OccurredOn = today.AddDays(-14), Method = DiaryMethod.Meeting, Direction = DiaryDirection.Incoming, ContactName = "Jordan Reyes", Summary = "Owner's attorney met us on site to walk the frontage.", Outcome = "Agreed to review the offer package within two weeks." });
        _db.ParcelNotes.Add(new ParcelNote { ParcelId = parcel.Id, Text = "Owner prefers contact by email in the evening." });
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Seeded demo parcel team, diary and notes.");
    }

    /// <summary>
    /// Rights of entry (one ending soon, one still requested), an engineer's plan review, a draft staff report and two acquisition
    /// requests on the demo bridge project. The plan-review gate stays off. Skipped once any right of entry exists.
    /// </summary>
    private async Task SeedAcquisitionRecordsAsync(CancellationToken ct)
    {
        if (await _db.RightsOfEntry.AnyAsync(ct)) return;
        var bridge = await _db.Projects.FirstOrDefaultAsync(p => p.Number == "DEMO-002", ct);
        if (bridge is null) return;
        var atOffer = await _db.Parcels.FirstOrDefaultAsync(p => p.Number == "001" && p.ProjectId == bridge.Id, ct);
        var late = await _db.Parcels.FirstOrDefaultAsync(p => p.Number == "002" && p.ProjectId == bridge.Id, ct);
        if (atOffer is null || late is null) return;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        _db.RightsOfEntry.AddRange(
            new RightOfEntry { ParcelId = atOffer.Id, Status = RightOfEntryStatus.Granted, RequestedOn = today.AddDays(-40), GrantedOn = today.AddDays(-30), ExpiresOn = today.AddDays(20), GrantedBy = "Alex Marlowe", Purpose = "Boundary survey and soil borings" },
            new RightOfEntry { ParcelId = late.Id, Status = RightOfEntryStatus.Requested, RequestedOn = today.AddDays(-5), Purpose = "Appraisal inspection" });
        _db.ParcelPlanReviews.Add(new ParcelPlanReview { ParcelId = atOffer.Id, Discipline = ReviewDiscipline.Engineer, Outcome = ReviewOutcome.Approved, ReviewedOn = today.AddDays(-10), ReviewerName = "Morgan Ellis, P.E." });
        _db.ParcelStaffReports.Add(new ParcelStaffReport { ParcelId = atOffer.Id, Kind = ReportKind.StaffReport, Title = "Staff report: 001 Harbor frontage", Body = "The parcel is needed for the bridge approach. The design cannot be moved without losing the required clearance." });
        _db.AcquisitionRequests.AddRange(
            new AcquisitionRequest { ProjectId = bridge.Id, RequesterType = RequesterType.Internal, RequesterName = "Design team", Property = "Vacant lot behind 112 Harbor Rd", DesiredParameters = "Temporary construction easement for 18 months", EstimatedAmount = 45_000m },
            new AcquisitionRequest { RequesterType = RequesterType.External, RequesterName = "Harbor Port Authority", RequesterEmail = "land@harborport.example", Property = "Parcel along the north quay", DesiredParameters = "Fee purchase for a shared-use path" });
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Seeded demo rights of entry, plan review, staff report and acquisition requests.");
    }

    /// <summary>
    /// Sample owners, a lienholder, an attorney, a tenant and an abutter around the bridge parcels. No tax numbers are seeded:
    /// try entering one on a party to see it masked. Skipped once any party exists.
    /// </summary>
    private async Task SeedPartiesAsync(CancellationToken ct)
    {
        if (await _db.Parties.AnyAsync(ct)) return;
        var tract = await _db.ParentTracts.FirstOrDefaultAsync(t => t.Number == "T-1001", ct);
        var bridgeId = await _db.Projects.Where(p => p.Number == "DEMO-002").Select(p => p.Id).FirstOrDefaultAsync(ct);
        var pier = await _db.Parcels.FirstOrDefaultAsync(p => p.Number == "003" && p.ProjectId == bridgeId, ct);
        var atOffer = await _db.Parcels.FirstOrDefaultAsync(p => p.Number == "001" && p.ProjectId == bridgeId, ct);
        if (tract is null || pier is null || atOffer is null) return;

        Party Org(string name, string city) => new() { Kind = PartyKind.Organization, Name = name, City = city, State = "WA", PostalCode = "98101", Phone = "206-555-0100" };
        Party Person(string name, string city) => new() { Kind = PartyKind.Person, Name = name, City = city, State = "WA", PostalCode = "98101", Email = $"{name.Split(' ')[0].ToLowerInvariant()}@example.test" };

        var harbor = Org("Harbor Holdings LLC", "Seattle");
        harbor.LegalName = "Harbor Holdings, L.L.C.";
        var alex = Person("Alex Marlowe", "Seattle");
        var bank = Org("First Harbor Bank", "Seattle");
        var lawyer = Person("Jordan Reyes", "Seattle");
        var tenant = Person("Casey Tran", "Tacoma");
        var neighbour = Person("Morgan Ellis", "Seattle");
        _db.Parties.AddRange(harbor, alex, bank, lawyer, tenant, neighbour);

        _db.PartyLinks.AddRange(
            new PartyLink { PartyId = harbor.Id, ParentTractId = tract.Id, Role = PartyRole.FeeOwner, SharePercent = 60m },
            new PartyLink { PartyId = alex.Id, ParentTractId = tract.Id, Role = PartyRole.FeeOwner, SharePercent = 40m },
            new PartyLink { PartyId = bank.Id, ParentTractId = tract.Id, Role = PartyRole.Lienholder, Notes = "Deed of trust recorded 2019." },
            new PartyLink { PartyId = lawyer.Id, ParentTractId = tract.Id, Role = PartyRole.Attorney, Notes = "Represents the owners in negotiation." },
            new PartyLink { PartyId = tenant.Id, ParcelId = atOffer.Id, Role = PartyRole.Tenant, LeaseOnFile = true, LeaseDetails = "Retail space, expires 2027-08-31", InsuranceDetails = "Renter's policy on file" },
            new PartyLink { PartyId = neighbour.Id, ParcelId = pier.Id, Role = PartyRole.Abutter });
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Seeded demo parties.");
    }
}
