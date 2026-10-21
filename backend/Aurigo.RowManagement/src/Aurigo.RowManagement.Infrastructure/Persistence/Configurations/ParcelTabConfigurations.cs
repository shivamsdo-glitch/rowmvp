using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Configurations;

public class ParcelContactLogEntryConfiguration : IEntityTypeConfiguration<ParcelContactLogEntry>
{
    public void Configure(EntityTypeBuilder<ParcelContactLogEntry> b)
    {
        b.ToTable("parcel_contact_log_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).IsRequired().HasMaxLength(80);
        b.Property(x => x.Mode).HasMaxLength(100);
        b.Property(x => x.ContactedBy).HasMaxLength(200);
        b.Property(x => x.ContactPerson).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Links).WithOne().HasForeignKey(l => l.ContactLogEntryId).OnDelete(DeleteBehavior.Restrict);
        // The sequence is never reused on a parcel, whatever races to add an entry.
        b.HasIndex(x => new { x.ParcelId, x.Sequence }).IsUnique();
        b.HasIndex(x => new { x.ParcelId, x.ContactedAt });
    }
}

public class ParcelContactLogLinkConfiguration : IEntityTypeConfiguration<ParcelContactLogLink>
{
    public void Configure(EntityTypeBuilder<ParcelContactLogLink> b)
    {
        b.ToTable("parcel_contact_log_links");
        b.HasKey(x => x.Id);
        b.Property(x => x.RecordIdentifier).IsRequired().HasMaxLength(100);
        b.Property(x => x.ModuleName).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(500);
    }
}

public class ParcelEasementConfiguration : IEntityTypeConfiguration<ParcelEasement>
{
    public void Configure(EntityTypeBuilder<ParcelEasement> b)
    {
        b.ToTable("parcel_easements");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).IsRequired().HasMaxLength(50);
        b.Property(x => x.EasementType).HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.StartStation).HasMaxLength(100);
        b.Property(x => x.EndStation).HasMaxLength(100);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ParcelId, x.Number }).IsUnique();
    }
}
