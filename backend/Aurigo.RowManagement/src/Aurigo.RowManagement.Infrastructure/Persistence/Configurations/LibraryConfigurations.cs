using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Configurations;

public class LibraryFieldConfiguration : IEntityTypeConfiguration<LibraryField>
{
    public void Configure(EntityTypeBuilder<LibraryField> b)
    {
        b.ToTable("library_fields");
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).IsRequired().HasMaxLength(40);
        b.Property(x => x.Label).IsRequired().HasMaxLength(100);
        b.Property(x => x.FieldType).HasConversion<string>().HasMaxLength(10);
        b.HasMany(x => x.Values).WithOne().HasForeignKey(v => v.LibraryFieldId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
    }
}

public class LibraryValueConfiguration : IEntityTypeConfiguration<LibraryValue>
{
    public void Configure(EntityTypeBuilder<LibraryValue> b)
    {
        b.ToTable("library_values");
        b.HasKey(x => x.Id);
        b.Property(x => x.Value).IsRequired().HasMaxLength(100);
        b.Property(x => x.NormalizedValue).IsRequired().HasMaxLength(100);
        // No two values of one field alike, ignoring case, whatever races to add them.
        b.HasIndex(x => new { x.LibraryFieldId, x.NormalizedValue }).IsUnique();
    }
}
