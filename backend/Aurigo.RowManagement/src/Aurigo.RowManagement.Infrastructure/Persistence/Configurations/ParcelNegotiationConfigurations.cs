using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Configurations;

public class ParcelNegotiationConfiguration : IEntityTypeConfiguration<ParcelNegotiation>
{
    public void Configure(EntityTypeBuilder<ParcelNegotiation> b)
    {
        b.ToTable("parcel_negotiations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).IsRequired().HasMaxLength(80);
        b.Property(x => x.AssignedTo).HasMaxLength(200);
        b.Property(x => x.JatResponse).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.TotalAgreementAmount).HasPrecision(18, 2);
        b.Property(x => x.PuaApprovedAmount).HasPrecision(18, 2);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.StatusNotes).WithOne().HasForeignKey(c => c.NegotiationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Agents).WithOne().HasForeignKey(c => c.NegotiationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.RightsOfEntry).WithOne().HasForeignKey(c => c.NegotiationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Offers).WithOne().HasForeignKey(c => c.NegotiationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Settlements).WithOne().HasForeignKey(c => c.NegotiationId).OnDelete(DeleteBehavior.Restrict);
        // One negotiation record per parcel, whatever races to create it.
        b.HasIndex(x => x.ParcelId).IsUnique();
    }
}

public class NegotiationNoteConfiguration : IEntityTypeConfiguration<NegotiationNote>
{
    public void Configure(EntityTypeBuilder<NegotiationNote> b)
    {
        b.ToTable("negotiation_notes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Text).IsRequired().HasMaxLength(4000);
    }
}

public class NegotiationAgentConfiguration : IEntityTypeConfiguration<NegotiationAgent>
{
    public void Configure(EntityTypeBuilder<NegotiationAgent> b)
    {
        b.ToTable("negotiation_agents");
        b.HasKey(x => x.Id);
        b.Property(x => x.AgentName).IsRequired().HasMaxLength(200);
    }
}

public class NegotiationRightOfEntryConfiguration : IEntityTypeConfiguration<NegotiationRightOfEntry>
{
    public void Configure(EntityTypeBuilder<NegotiationRightOfEntry> b)
    {
        b.ToTable("negotiation_rights_of_entry");
        b.HasKey(x => x.Id);
        b.Property(x => x.SpecialInstructions).HasMaxLength(2000);
    }
}

public class NegotiationOfferConfiguration : IEntityTypeConfiguration<NegotiationOffer>
{
    public void Configure(EntityTypeBuilder<NegotiationOffer> b)
    {
        b.ToTable("negotiation_offers");
        b.HasKey(x => x.Id);
        b.Property(x => x.OfferType).HasMaxLength(100);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Notes).HasMaxLength(2000);
    }
}

public class NegotiationSettlementConfiguration : IEntityTypeConfiguration<NegotiationSettlement>
{
    public void Configure(EntityTypeBuilder<NegotiationSettlement> b)
    {
        b.ToTable("negotiation_settlements");
        b.HasKey(x => x.Id);
        b.Property(x => x.SettlementType).HasMaxLength(100);
        b.Property(x => x.CounterofferResponse).HasMaxLength(100);
        b.Property(x => x.CounterofferAmount).HasPrecision(18, 2);
        b.Property(x => x.SettlementNotes).HasMaxLength(2000);
    }
}
