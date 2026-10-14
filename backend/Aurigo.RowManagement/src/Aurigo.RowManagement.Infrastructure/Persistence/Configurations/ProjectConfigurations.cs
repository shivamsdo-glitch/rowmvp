using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.ToTable("projects");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).IsRequired().HasMaxLength(50);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Region).HasMaxLength(100);
        b.Property(x => x.District).HasMaxLength(100);
        b.Property(x => x.OversightType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.FundingSource).HasMaxLength(200);
        b.Property(x => x.FundingAmount).HasPrecision(18, 2);
        b.Property(x => x.DesignEstimate).HasPrecision(18, 2);
        b.Property(x => x.NepaDecisionType).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.ExceptionType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ExceptionReason).HasMaxLength(1000);
        // The project number is unique within a tenant (ROW-001).
        b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Region, x.District });
    }
}

public class ProjectCostEstimateConfiguration : IEntityTypeConfiguration<ProjectCostEstimate>
{
    public void Configure(EntityTypeBuilder<ProjectCostEstimate> b)
    {
        b.ToTable("project_cost_estimates");
        b.HasKey(x => x.Id);
        b.Property(x => x.LandCost).HasPrecision(18, 2);
        b.Property(x => x.ImprovementsCost).HasPrecision(18, 2);
        b.Property(x => x.RelocationCost).HasPrecision(18, 2);
        b.Property(x => x.IndirectCost).HasPrecision(18, 2);
        b.Property(x => x.Total).HasPrecision(18, 2);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasOne<Project>().WithMany(p => p.Estimates).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ProjectId, x.Version }).IsUnique();
    }
}

public class ProjectBudgetConfiguration : IEntityTypeConfiguration<ProjectBudget>
{
    public void Configure(EntityTypeBuilder<ProjectBudget> b)
    {
        b.ToTable("project_budgets");
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasOne<Project>().WithMany(p => p.Budgets).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ProjectId, x.FiscalYear }).IsUnique();
    }
}

public class RowAuthorizationConfiguration : IEntityTypeConfiguration<RowAuthorization>
{
    public void Configure(EntityTypeBuilder<RowAuthorization> b)
    {
        b.ToTable("row_authorizations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.AuthorizedAmount).HasPrecision(18, 2);
        b.Property(x => x.FederalSharePercent).HasPrecision(5, 2);
        b.Property(x => x.ReferenceNumber).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasOne<Project>().WithMany(p => p.Authorizations).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.ProjectId);
    }
}

public class ProjectNumberSettingsConfiguration : IEntityTypeConfiguration<ProjectNumberSettings>
{
    public void Configure(EntityTypeBuilder<ProjectNumberSettings> b)
    {
        b.ToTable("project_number_settings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Pattern).IsRequired().HasMaxLength(100);
        b.HasIndex(x => x.TenantId).IsUnique(); // one row per tenant
    }
}

public class ProjectNumberCounterConfiguration : IEntityTypeConfiguration<ProjectNumberCounter>
{
    public void Configure(EntityTypeBuilder<ProjectNumberCounter> b)
    {
        b.ToTable("project_number_counters");
        b.HasKey(x => x.Id);
        b.Property(x => x.ScopeKey).IsRequired().HasMaxLength(20);
        // Postgres system column xmin changes on every update, which makes it a free optimistic-concurrency token.
        b.Property(x => x.Version).HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.ScopeKey }).IsUnique();
    }
}
