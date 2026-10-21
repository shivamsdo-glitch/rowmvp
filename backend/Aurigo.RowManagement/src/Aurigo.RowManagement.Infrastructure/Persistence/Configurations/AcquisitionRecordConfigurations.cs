using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Configurations;

public class RightOfEntryConfiguration : IEntityTypeConfiguration<RightOfEntry>
{
    public void Configure(EntityTypeBuilder<RightOfEntry> b)
    {
        b.ToTable("rights_of_entry", t => t.HasCheckConstraint("ck_rights_of_entry_dates", "expires_on IS NULL OR granted_on IS NULL OR expires_on >= granted_on"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.GrantedBy).HasMaxLength(200);
        b.Property(x => x.Purpose).HasMaxLength(500);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ParcelId);
        b.HasIndex(x => new { x.TenantId, x.Status, x.ExpiresOn });
    }
}

public class AcquisitionRequestConfiguration : IEntityTypeConfiguration<AcquisitionRequest>
{
    public void Configure(EntityTypeBuilder<AcquisitionRequest> b)
    {
        b.ToTable("acquisition_requests");
        b.HasKey(x => x.Id);
        b.Property(x => x.RequesterType).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.RequesterName).IsRequired().HasMaxLength(200);
        b.Property(x => x.RequesterEmail).HasMaxLength(200);
        b.Property(x => x.Property).IsRequired().HasMaxLength(500);
        b.Property(x => x.DesiredParameters).HasMaxLength(2000);
        b.Property(x => x.EstimatedAmount).HasPrecision(18, 2);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Version).HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        b.Property(x => x.DecisionNote).HasMaxLength(1000);
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
    }
}

public class ParcelPlanReviewConfiguration : IEntityTypeConfiguration<ParcelPlanReview>
{
    public void Configure(EntityTypeBuilder<ParcelPlanReview> b)
    {
        b.ToTable("parcel_plan_reviews");
        b.HasKey(x => x.Id);
        b.Property(x => x.Discipline).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ReviewerName).IsRequired().HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ParcelId, x.Discipline });
    }
}

public class ParcelStaffReportConfiguration : IEntityTypeConfiguration<ParcelStaffReport>
{
    public void Configure(EntityTypeBuilder<ParcelStaffReport> b)
    {
        b.ToTable("parcel_staff_reports");
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Title).IsRequired().HasMaxLength(200);
        b.Property(x => x.Body).IsRequired().HasMaxLength(8000);
        b.Property(x => x.Version).HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        b.Property(x => x.DecisionNote).HasMaxLength(1000);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        // One live report of each kind per parcel: a rejected one may be replaced, any other blocks a second.
        b.HasIndex(x => new { x.ParcelId, x.Kind }).IsUnique().HasFilter("status <> 'Rejected'");
    }
}
