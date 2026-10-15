using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Configurations;

public class ParcelStatusConfiguration : IEntityTypeConfiguration<ParcelStatus>
{
    public void Configure(EntityTypeBuilder<ParcelStatus> b)
    {
        b.ToTable("parcel_statuses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(60);
        b.Property(x => x.Color).IsRequired().HasMaxLength(7);
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public class ParcelGroupConfiguration : IEntityTypeConfiguration<ParcelGroup>
{
    public void Configure(EntityTypeBuilder<ParcelGroup> b)
    {
        b.ToTable("parcel_groups");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
    }
}

public class ParentTractConfiguration : IEntityTypeConfiguration<ParentTract>
{
    public void Configure(EntityTypeBuilder<ParentTract> b)
    {
        b.ToTable("parent_tracts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).IsRequired().HasMaxLength(50);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Version).HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ProjectId, x.Number }).IsUnique();
    }
}

public class ParcelConfiguration : IEntityTypeConfiguration<Parcel>
{
    public void Configure(EntityTypeBuilder<Parcel> b)
    {
        b.ToTable("parcels");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).IsRequired().HasMaxLength(50);
        b.Property(x => x.LegalDescription).HasMaxLength(4000);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.AreaSqFt).HasPrecision(18, 2);
        b.Property(x => x.PropertyType).HasMaxLength(100);
        b.Property(x => x.AcquisitionType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.CurrentStepKey).IsRequired().HasMaxLength(60);
        b.Property(x => x.State).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.NoticeMethod).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.CustomValuesJson).IsRequired().HasColumnType("jsonb");
        // Postgres system column xmin changes on every update: a free optimistic-concurrency token (see Parcel.Version).
        b.Property(x => x.Version).HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ParentTract>().WithMany().HasForeignKey(x => x.ParentTractId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ParcelGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ParcelStatus>().WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.Restrict);

        // A parcel number is never reused within a project, even after the parcel is split or combined away.
        b.HasIndex(x => new { x.ProjectId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.State });
        b.HasIndex(x => x.StatusId);
        b.HasIndex(x => x.GroupId);
        b.HasIndex(x => x.ParentTractId);
    }
}

public class ParcelInterestConfiguration : IEntityTypeConfiguration<ParcelInterest>
{
    public void Configure(EntityTypeBuilder<ParcelInterest> b)
    {
        b.ToTable("parcel_interests");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.AreaSqFt).HasPrecision(18, 2);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasOne<Parcel>().WithMany(p => p.Interests).HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.ParcelId);
    }
}

public class ParcelMilestoneConfiguration : IEntityTypeConfiguration<ParcelMilestone>
{
    public void Configure(EntityTypeBuilder<ParcelMilestone> b)
    {
        b.ToTable("parcel_milestones");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasOne<Parcel>().WithMany(p => p.Milestones).HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ParcelId, x.Name }).IsUnique();
    }
}

public class ParcelSiteVisitConfiguration : IEntityTypeConfiguration<ParcelSiteVisit>
{
    public void Configure(EntityTypeBuilder<ParcelSiteVisit> b)
    {
        b.ToTable("parcel_site_visits");
        b.HasKey(x => x.Id);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasOne<Parcel>().WithMany(p => p.SiteVisits).HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.ParcelId);
    }
}

public class ParcelStepEventConfiguration : IEntityTypeConfiguration<ParcelStepEvent>
{
    public void Configure(EntityTypeBuilder<ParcelStepEvent> b)
    {
        b.ToTable("parcel_step_events");
        b.HasKey(x => x.Id);
        b.Property(x => x.FromStepKey).HasMaxLength(60);
        b.Property(x => x.ToStepKey).IsRequired().HasMaxLength(60);
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasOne<Parcel>().WithMany(p => p.StepEvents).HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.ParcelId);
    }
}

public class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> b)
    {
        b.ToTable("custom_field_definitions");
        b.HasKey(x => x.Id);
        b.Property(x => x.EntityType).IsRequired().HasMaxLength(30);
        b.Property(x => x.Key).IsRequired().HasMaxLength(40);
        b.Property(x => x.Label).IsRequired().HasMaxLength(100);
        b.Property(x => x.FieldType).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.TenantId, x.EntityType, x.Key }).IsUnique();
    }
}

public class WorkflowTemplateConfiguration : IEntityTypeConfiguration<WorkflowTemplate>
{
    public void Configure(EntityTypeBuilder<WorkflowTemplate> b)
    {
        b.ToTable("workflow_templates");
        b.HasKey(x => x.Id);
        b.Property(x => x.AcquisitionType).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(x => new { x.TenantId, x.AcquisitionType }).IsUnique();
    }
}

public class WorkflowStepConfiguration : IEntityTypeConfiguration<WorkflowStep>
{
    public void Configure(EntityTypeBuilder<WorkflowStep> b)
    {
        b.ToTable("workflow_steps");
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).IsRequired().HasMaxLength(60);
        b.Property(x => x.Label).IsRequired().HasMaxLength(100);
        b.HasOne<WorkflowTemplate>().WithMany(t => t.Steps).HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TemplateId, x.Key }).IsUnique();
    }
}

public class DeadlineRuleConfiguration : IEntityTypeConfiguration<DeadlineRule>
{
    public void Configure(EntityTypeBuilder<DeadlineRule> b)
    {
        b.ToTable("deadline_rules");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(150);
        b.Property(x => x.TriggerMilestone).IsRequired().HasMaxLength(100);
        b.Property(x => x.TargetMilestone).IsRequired().HasMaxLength(100);
        b.HasIndex(x => x.TenantId);
    }
}

public class StepGateSettingsConfiguration : IEntityTypeConfiguration<StepGateSettings>
{
    public void Configure(EntityTypeBuilder<StepGateSettings> b)
    {
        b.ToTable("step_gate_settings");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.TenantId).IsUnique(); // one row per tenant
    }
}
