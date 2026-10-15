using System.Reflection;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Infrastructure.Persistence;

public class RowDbContext : DbContext, IRowDbContext
{
    /// <summary>
    /// The tenant every query is scoped to. An INSTANCE property (not a static) so EF Core
    /// parameterizes it per query execution; a static would be constant-folded into cached query
    /// plans and leak across tenants. Initialised from the request's JWT; seeders reassign it to
    /// switch tenant context. Guid.Empty matches no rows, so an unresolved tenant fails closed.
    /// </summary>
    public Guid CurrentTenantId { get; set; }

    public RowDbContext(DbContextOptions<RowDbContext> options, ICurrentTenant currentTenant)
        : base(options)
    {
        CurrentTenantId = currentTenant.IsAvailable ? currentTenant.Value : Guid.Empty;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePagePermission> RolePagePermissions => Set<RolePagePermission>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<SavedFilterProfile> SavedFilterProfiles => Set<SavedFilterProfile>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectCostEstimate> ProjectCostEstimates => Set<ProjectCostEstimate>();
    public DbSet<ProjectBudget> ProjectBudgets => Set<ProjectBudget>();
    public DbSet<RowAuthorization> RowAuthorizations => Set<RowAuthorization>();
    public DbSet<ProjectNumberSettings> ProjectNumberSettings => Set<ProjectNumberSettings>();
    public DbSet<AgencySettings> AgencySettings => Set<AgencySettings>();
    public DbSet<ProjectNumberCounter> ProjectNumberCounters => Set<ProjectNumberCounter>();
    public DbSet<ParcelStatus> ParcelStatuses => Set<ParcelStatus>();
    public DbSet<ParcelGroup> ParcelGroups => Set<ParcelGroup>();
    public DbSet<ParentTract> ParentTracts => Set<ParentTract>();
    public DbSet<Parcel> Parcels => Set<Parcel>();
    public DbSet<ParcelInterest> ParcelInterests => Set<ParcelInterest>();
    public DbSet<ParcelMilestone> ParcelMilestones => Set<ParcelMilestone>();
    public DbSet<ParcelSiteVisit> ParcelSiteVisits => Set<ParcelSiteVisit>();
    public DbSet<ParcelStepEvent> ParcelStepEvents => Set<ParcelStepEvent>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<WorkflowTemplate> WorkflowTemplates => Set<WorkflowTemplate>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<DeadlineRule> DeadlineRules => Set<DeadlineRule>();
    public DbSet<StepGateSettings> StepGateSettings => Set<StepGateSettings>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<LibraryField> LibraryFields => Set<LibraryField>();
    public DbSet<LibraryValue> LibraryValues => Set<LibraryValue>();
    public DbSet<PartyLink> PartyLinks => Set<PartyLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Tenant isolation: one global filter (TenantId == CurrentTenantId) on every tenant-owned entity.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
                ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, new object[] { modelBuilder });
        }

        base.OnModelCreating(modelBuilder);
    }

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(RowDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private void ApplyTenantFilter<T>(ModelBuilder builder) where T : class, ITenantOwned =>
        builder.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
}
