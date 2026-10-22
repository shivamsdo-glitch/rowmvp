using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Configurations;

public class RecordStatusNoteConfiguration : IEntityTypeConfiguration<RecordStatusNote>
{
    public void Configure(EntityTypeBuilder<RecordStatusNote> b)
    {
        b.ToTable("record_status_notes");
        b.HasKey(x => x.Id);
        b.Property(x => x.OwnerKind).IsRequired().HasMaxLength(40);
        b.Property(x => x.Text).IsRequired().HasMaxLength(4000);
        b.HasIndex(x => new { x.OwnerKind, x.OwnerId });
    }
}

public class ParcelCondemnationConfiguration : IEntityTypeConfiguration<ParcelCondemnation>
{
    public void Configure(EntityTypeBuilder<ParcelCondemnation> b)
    {
        b.ToTable("parcel_condemnations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).IsRequired().HasMaxLength(80);
        b.Property(x => x.EdCoordinator).HasMaxLength(200);
        b.Property(x => x.OutsideCounsel).HasMaxLength(200);
        b.Property(x => x.AppraisalWitness).HasMaxLength(200);
        b.Property(x => x.EngineeringWitness).HasMaxLength(200);
        b.Property(x => x.ExpertWitness1).HasMaxLength(200);
        b.Property(x => x.ExpertWitness2).HasMaxLength(200);
        b.Property(x => x.ProbateCauseNo).HasMaxLength(100);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Attorneys).WithOne().HasForeignKey(c => c.CondemnationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Commissioners).WithOne().HasForeignKey(c => c.CondemnationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Notices).WithOne().HasForeignKey(c => c.CondemnationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ParcelId).IsUnique();
    }
}

public class CondemnationAttorneyConfiguration : IEntityTypeConfiguration<CondemnationAttorney>
{
    public void Configure(EntityTypeBuilder<CondemnationAttorney> b)
    {
        b.ToTable("condemnation_attorneys");
        b.HasKey(x => x.Id);
        b.Property(x => x.PartyRepresented).IsRequired().HasMaxLength(200);
        b.Property(x => x.AttorneyName).HasMaxLength(200);
        b.Property(x => x.Firm).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Phone).HasMaxLength(100);
        b.Property(x => x.Email).HasMaxLength(200);
    }
}

public class CondemnationCommissionerConfiguration : IEntityTypeConfiguration<CondemnationCommissioner>
{
    public void Configure(EntityTypeBuilder<CondemnationCommissioner> b)
    {
        b.ToTable("condemnation_commissioners");
        b.HasKey(x => x.Id);
        b.Property(x => x.AppointmentType).HasMaxLength(100);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(100);
    }
}

public class CondemnationNoticeConfiguration : IEntityTypeConfiguration<CondemnationNotice>
{
    public void Configure(EntityTypeBuilder<CondemnationNotice> b)
    {
        b.ToTable("condemnation_notices");
        b.HasKey(x => x.Id);
        b.Property(x => x.PartyToServe).IsRequired().HasMaxLength(200);
        b.Property(x => x.Comments).HasMaxLength(2000);
    }
}

public class ParcelPropertyRecordConfiguration : IEntityTypeConfiguration<ParcelPropertyRecord>
{
    public void Configure(EntityTypeBuilder<ParcelPropertyRecord> b)
    {
        b.ToTable("parcel_property_records");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).IsRequired().HasMaxLength(80);
        b.Property(x => x.DemoStatus).HasMaxLength(100);
        b.Property(x => x.DispositionType).HasMaxLength(100);
        b.Property(x => x.DispositionMethod).HasMaxLength(100);
        b.Property(x => x.DispositionStatus).HasMaxLength(100);
        b.Property(x => x.DispositionJustification).HasMaxLength(4000);
        b.Property(x => x.BuyerGrantee).HasMaxLength(1000);
        b.Property(x => x.DocumentRecordingNo).HasMaxLength(1000);
        b.Property(x => x.SalePrice).HasPrecision(18, 2);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Funding).WithOne().HasForeignKey(c => c.PropertyRecordId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ParcelId).IsUnique();
    }
}

public class PropertyFundingConfiguration : IEntityTypeConfiguration<PropertyFunding>
{
    public void Configure(EntityTypeBuilder<PropertyFunding> b)
    {
        b.ToTable("property_funding");
        b.HasKey(x => x.Id);
        b.Property(x => x.FundType).IsRequired().HasMaxLength(100);
        b.Property(x => x.FundSource).HasMaxLength(200);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Percentage).HasPrecision(7, 4);
    }
}

public class ParcelRelocationConfiguration : IEntityTypeConfiguration<ParcelRelocation>
{
    public void Configure(EntityTypeBuilder<ParcelRelocation> b)
    {
        b.ToTable("parcel_relocations");
        b.HasKey(x => x.Id);
        b.Property(x => x.RelocationNumber).IsRequired().HasMaxLength(100);
        b.Property(x => x.AssignedTo).HasMaxLength(200);
        b.Property(x => x.DisplaceeName).IsRequired().HasMaxLength(200);
        b.Property(x => x.DisplaceeStatus).HasMaxLength(100);
        b.Property(x => x.DisplaceeInterest).HasMaxLength(100);
        b.Property(x => x.DisplaceeMailingAddress).HasMaxLength(1000);
        b.Property(x => x.RelocationType).IsRequired().HasMaxLength(100);
        b.Property(x => x.PropertySiteAddress).HasMaxLength(1000);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Agents).WithOne().HasForeignKey(c => c.RelocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Contacts).WithOne().HasForeignKey(c => c.RelocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Eligibilities).WithOne().HasForeignKey(c => c.RelocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.ContactLog).WithOne().HasForeignKey(c => c.RelocationId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.AdvisoryNeeds).HasMaxLength(4000);
        b.Property(x => x.EligibilityException).HasMaxLength(100);
        b.Property(x => x.AppealOutcome).HasMaxLength(1000);
        b.HasMany(x => x.Payments).WithOne().HasForeignKey(c => c.RelocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ParcelId, x.RelocationNumber }).IsUnique();
        b.HasIndex(x => x.PartyLinkId);
    }
}

public class RelocationPaymentConfiguration : IEntityTypeConfiguration<RelocationPayment>
{
    public void Configure(EntityTypeBuilder<RelocationPayment> b)
    {
        b.ToTable("relocation_payments");
        b.HasKey(x => x.Id);
        b.Property(x => x.PaymentType).IsRequired().HasMaxLength(100);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RequiredRoles).IsRequired().HasMaxLength(500);
        b.Property(x => x.DecisionNote).HasMaxLength(1000);
        b.Property(x => x.PaymentReference).HasMaxLength(200);
        b.HasMany(x => x.Approvals).WithOne().HasForeignKey(c => c.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RelocationPaymentApprovalConfiguration : IEntityTypeConfiguration<RelocationPaymentApproval>
{
    public void Configure(EntityTypeBuilder<RelocationPaymentApproval> b)
    {
        b.ToTable("relocation_payment_approvals");
        b.HasKey(x => x.Id);
        b.Property(x => x.Role).IsRequired().HasMaxLength(100);
    }
}

public class RelocationPaymentBandConfiguration : IEntityTypeConfiguration<RelocationPaymentBand>
{
    public void Configure(EntityTypeBuilder<RelocationPaymentBand> b)
    {
        b.ToTable("relocation_payment_bands");
        b.HasKey(x => x.Id);
        b.Property(x => x.MinAmount).HasPrecision(18, 2);
        b.Property(x => x.MaxAmount).HasPrecision(18, 2);
        b.Property(x => x.Roles).IsRequired().HasMaxLength(500);
    }
}

public class RelocationAgentRowConfiguration : IEntityTypeConfiguration<RelocationAgentRow>
{
    public void Configure(EntityTypeBuilder<RelocationAgentRow> b)
    {
        b.ToTable("relocation_agents");
        b.HasKey(x => x.Id);
        b.Property(x => x.AgentName).IsRequired().HasMaxLength(200);
    }
}

public class RelocationContactConfiguration : IEntityTypeConfiguration<RelocationContact>
{
    public void Configure(EntityTypeBuilder<RelocationContact> b)
    {
        b.ToTable("relocation_contacts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.ContactType).HasMaxLength(100);
        b.Property(x => x.Contact).HasMaxLength(200);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(500);
    }
}

public class RelocationEligibilityConfiguration : IEntityTypeConfiguration<RelocationEligibility>
{
    public void Configure(EntityTypeBuilder<RelocationEligibility> b)
    {
        b.ToTable("relocation_eligibilities");
        b.HasKey(x => x.Id);
        b.Property(x => x.EligibilityType).IsRequired().HasMaxLength(100);
        b.Property(x => x.ClaimLimit).HasPrecision(18, 2);
    }
}

public class RelocationContactLogRowConfiguration : IEntityTypeConfiguration<RelocationContactLogRow>
{
    public void Configure(EntityTypeBuilder<RelocationContactLogRow> b)
    {
        b.ToTable("relocation_contact_log");
        b.HasKey(x => x.Id);
        b.Property(x => x.Mode).HasMaxLength(100);
        b.Property(x => x.ContactedBy).HasMaxLength(200);
        b.Property(x => x.ContactPersons).HasMaxLength(500);
        b.Property(x => x.Notes).HasMaxLength(4000);
    }
}
