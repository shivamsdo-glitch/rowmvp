using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Persistence;

/// <summary>
/// The slice of the EF context that handlers use. Every tenant-owned set is already scoped to the
/// current tenant by a global query filter; only anonymous auth flows call IgnoreQueryFilters().
/// </summary>
public interface IRowDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<AppUser> AppUsers { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePagePermission> RolePagePermissions { get; }
    DbSet<UserToken> UserTokens { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Document> Documents { get; }
    DbSet<SavedFilterProfile> SavedFilterProfiles { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
