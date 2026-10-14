using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Configurations;

public class RolePagePermissionConfiguration : IEntityTypeConfiguration<RolePagePermission>
{
    public void Configure(EntityTypeBuilder<RolePagePermission> b)
    {
        b.ToTable("role_page_permissions");
        b.HasKey(x => x.Id);
        b.Property(x => x.PageKey).IsRequired().HasMaxLength(60);
        b.Property(x => x.Template).IsRequired().HasMaxLength(20);
        b.HasOne(x => x.Role).WithMany(r => r.Permissions).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        // One row per (role, page).
        b.HasIndex(x => new { x.RoleId, x.PageKey }).IsUnique();
        b.HasIndex(x => x.TenantId);
    }
}

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.ToTable("documents");
        b.HasKey(x => x.Id);
        b.Property(x => x.EntityType).IsRequired().HasMaxLength(60);
        b.Property(x => x.FileName).IsRequired().HasMaxLength(200);
        b.Property(x => x.ContentType).IsRequired().HasMaxLength(100);
        b.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
        b.Property(x => x.DocType).HasMaxLength(60);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.ExternalDocId).IsRequired().HasMaxLength(200);
        // The list screen for an entity: its live attachments.
        b.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId, x.IsDeleted });
    }
}

public class SavedFilterProfileConfiguration : IEntityTypeConfiguration<SavedFilterProfile>
{
    public void Configure(EntityTypeBuilder<SavedFilterProfile> b)
    {
        b.ToTable("saved_filter_profiles");
        b.HasKey(x => x.Id);
        b.Property(x => x.PageKey).IsRequired().HasMaxLength(60);
        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        b.Property(x => x.FilterJson).IsRequired().HasColumnType("jsonb");
        b.HasIndex(x => new { x.TenantId, x.UserId, x.PageKey, x.Name }).IsUnique();
    }
}
