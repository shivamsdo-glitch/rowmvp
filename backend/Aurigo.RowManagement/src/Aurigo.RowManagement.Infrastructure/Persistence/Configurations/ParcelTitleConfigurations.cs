using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Configurations;

public class ParcelEncumbranceConfiguration : IEntityTypeConfiguration<ParcelEncumbrance>
{
    public void Configure(EntityTypeBuilder<ParcelEncumbrance> b)
    {
        b.ToTable("parcel_encumbrances");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).IsRequired().HasMaxLength(80);
        b.Property(x => x.EncumbranceType).IsRequired().HasMaxLength(100);
        b.Property(x => x.DocumentTitle).IsRequired().HasMaxLength(300);
        b.Property(x => x.DocumentNumber).HasMaxLength(500);
        b.Property(x => x.Grantor).IsRequired().HasMaxLength(1000);
        b.Property(x => x.Grantee).IsRequired().HasMaxLength(1000);
        b.Property(x => x.ImpactAnalysis).HasMaxLength(4000);
        b.Property(x => x.RecommendedCure).HasMaxLength(4000);
        b.Property(x => x.CureStatus).HasMaxLength(100);
        b.Property(x => x.ReviewNotes).HasMaxLength(4000);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Links).WithOne().HasForeignKey(l => l.EncumbranceId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ParcelId, x.Sequence }).IsUnique();
    }
}

public class ParcelEncumbranceLinkConfiguration : IEntityTypeConfiguration<ParcelEncumbranceLink>
{
    public void Configure(EntityTypeBuilder<ParcelEncumbranceLink> b)
    {
        b.ToTable("parcel_encumbrance_links");
        b.HasKey(x => x.Id);
        b.Property(x => x.RecordIdentifier).IsRequired().HasMaxLength(100);
        b.Property(x => x.ModuleName).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(500);
    }
}

public class ParcelTitleClosingConfiguration : IEntityTypeConfiguration<ParcelTitleClosing>
{
    public void Configure(EntityTypeBuilder<ParcelTitleClosing> b)
    {
        b.ToTable("parcel_title_closings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).IsRequired().HasMaxLength(80);
        b.Property(x => x.AssignedTo).HasMaxLength(200);
        b.Property(x => x.TitleCompany).HasMaxLength(200);
        b.Property(x => x.TitleClosingAgent).HasMaxLength(200);
        b.Property(x => x.GfNumber).HasMaxLength(100);
        b.Property(x => x.FinalTitlePolicyNo).HasMaxLength(100);
        b.Property(x => x.PsaAmount).HasPrecision(18, 2);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.ConveyanceDocuments).WithOne().HasForeignKey(c => c.TitleClosingId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.TitleCommitments).WithOne().HasForeignKey(c => c.TitleClosingId).OnDelete(DeleteBehavior.Restrict);
        // One title and closing record per parcel, whatever races to create it.
        b.HasIndex(x => x.ParcelId).IsUnique();
    }
}

public class ParcelConveyanceDocumentConfiguration : IEntityTypeConfiguration<ParcelConveyanceDocument>
{
    public void Configure(EntityTypeBuilder<ParcelConveyanceDocument> b)
    {
        b.ToTable("parcel_conveyance_documents");
        b.HasKey(x => x.Id);
        b.Property(x => x.ConveyanceType).HasMaxLength(100);
        b.Property(x => x.RecordingNumber).HasMaxLength(100);
    }
}

public class ParcelTitleCommitmentConfiguration : IEntityTypeConfiguration<ParcelTitleCommitment>
{
    public void Configure(EntityTypeBuilder<ParcelTitleCommitment> b)
    {
        b.ToTable("parcel_title_commitments");
        b.HasKey(x => x.Id);
        b.Property(x => x.VestedOwnerName).IsRequired().HasMaxLength(200);
    }
}
