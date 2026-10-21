using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Configurations;

public class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> b)
    {
        b.ToTable("parties");
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.LegalName).HasMaxLength(300);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.AddressLine1).HasMaxLength(200);
        b.Property(x => x.AddressLine2).HasMaxLength(200);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.State).HasMaxLength(2);
        b.Property(x => x.PostalCode).HasMaxLength(10);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.TaxIdType).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.TaxIdProtected).HasMaxLength(200);
        b.Property(x => x.TaxIdLast4).HasMaxLength(4);
        b.HasIndex(x => new { x.TenantId, x.Name });
        b.HasMany(x => x.Addresses).WithOne().HasForeignKey(a => a.PartyId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Contacts).WithOne().HasForeignKey(c => c.PartyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PartyAddressConfiguration : IEntityTypeConfiguration<PartyAddress>
{
    public void Configure(EntityTypeBuilder<PartyAddress> b)
    {
        b.ToTable("party_addresses");
        b.HasKey(x => x.Id);
        b.Property(x => x.AddressType).HasMaxLength(100);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.ContactPerson).HasMaxLength(200);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.State).HasMaxLength(50);
        b.Property(x => x.Zip).HasMaxLength(20);
        b.HasIndex(x => x.PartyId);
    }
}

public class PartyContactConfiguration : IEntityTypeConfiguration<PartyContact>
{
    public void Configure(EntityTypeBuilder<PartyContact> b)
    {
        b.ToTable("party_contacts");
        b.HasKey(x => x.Id);
        b.Property(x => x.ContactType).HasMaxLength(100);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(300);
        b.HasIndex(x => x.PartyId);
    }
}

public class PartyLinkConfiguration : IEntityTypeConfiguration<PartyLink>
{
    public void Configure(EntityTypeBuilder<PartyLink> b)
    {
        b.ToTable("party_links", t => t.HasCheckConstraint("ck_party_links_one_target", "(parent_tract_id IS NULL) <> (parcel_id IS NULL)"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.SharePercent).HasPrecision(7, 4);
        b.Property(x => x.OwnerType).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.LeaseDetails).HasMaxLength(1000);
        b.Property(x => x.InsuranceDetails).HasMaxLength(1000);

        // A party that is still linked anywhere cannot be deleted.
        b.HasOne<Party>().WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ParentTract>().WithMany().HasForeignKey(x => x.ParentTractId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Parcel>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);

        // One party holds one role once per tract or parcel, whatever races to add it.
        b.HasIndex(x => new { x.ParcelId, x.PartyId, x.Role }).IsUnique().HasFilter("parcel_id IS NOT NULL");
        b.HasIndex(x => new { x.ParentTractId, x.PartyId, x.Role }).IsUnique().HasFilter("parent_tract_id IS NOT NULL");
        b.HasIndex(x => x.PartyId);
    }
}
