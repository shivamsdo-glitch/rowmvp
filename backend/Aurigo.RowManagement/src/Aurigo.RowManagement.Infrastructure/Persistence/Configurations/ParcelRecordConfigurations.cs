using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Configurations;

public class ParcelTeamMemberConfiguration : IEntityTypeConfiguration<ParcelTeamMember>
{
    public void Configure(EntityTypeBuilder<ParcelTeamMember> b)
    {
        b.ToTable("parcel_team_members");
        b.HasKey(x => x.Id);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.NameKey).IsRequired().HasMaxLength(200);
        b.Property(x => x.Organization).HasMaxLength(200);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        // One person holds one role once on a parcel, whatever races to add them.
        b.HasIndex(x => new { x.ParcelId, x.Role, x.NameKey }).IsUnique();
    }
}

public class ParcelDiaryEntryConfiguration : IEntityTypeConfiguration<ParcelDiaryEntry>
{
    public void Configure(EntityTypeBuilder<ParcelDiaryEntry> b)
    {
        b.ToTable("parcel_diary_entries");
        b.HasKey(x => x.Id);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.ContactName).IsRequired().HasMaxLength(200);
        b.Property(x => x.Summary).IsRequired().HasMaxLength(2000);
        b.Property(x => x.Outcome).HasMaxLength(1000);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Party>().WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ParcelId, x.OccurredOn });
    }
}

public class ParcelNoteConfiguration : IEntityTypeConfiguration<ParcelNote>
{
    public void Configure(EntityTypeBuilder<ParcelNote> b)
    {
        b.ToTable("parcel_notes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Text).IsRequired().HasMaxLength(4000);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ParcelId);
    }
}
