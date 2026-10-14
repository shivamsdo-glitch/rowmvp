using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Seed;

/// <summary>
/// Gives a tenant its default roles and page permissions. Used by the seed runner at startup and,
/// later, by whatever creates a new tenant. Idempotent and non-destructive: it only ADDS missing roles
/// and missing (role, page) rows, so an administrator's edits survive and pages added in a later
/// release are back-filled with their defaults.
/// </summary>
public sealed class TenantProvisioner
{
    private readonly RowDbContext _db;
    public TenantProvisioner(RowDbContext db) => _db = db;

    /// <summary>Returns how many role and permission rows were added.</summary>
    public async Task<int> EnsureDefaultRolesAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Explicit tenant predicates (not the ambient filter) so this works for any tenant, whatever
        // the context's current tenant is.
        var roles = await _db.Roles.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).ToListAsync(ct);
        var added = 0;

        foreach (var name in RoleNames.All)
        {
            if (roles.Any(r => r.Name == name)) continue;
            var role = new Role { TenantId = tenantId, Name = name, Description = DefaultRolePermissions.DescriptionFor(name), };
            _db.Roles.Add(role);
            roles.Add(role);
            added++;
        }

        var existing = (await _db.RolePagePermissions.IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId)
                .Select(p => new { p.RoleId, p.PageKey })
                .ToListAsync(ct))
            .Select(p => (p.RoleId, p.PageKey))
            .ToHashSet();

        foreach (var role in roles.Where(r => RoleNames.IsKnown(r.Name)))
        {
            foreach (var page in PageKeys.All)
            {
                if (existing.Contains((role.Id, page))) continue;

                var template = DefaultRolePermissions.TemplateFor(role.Name, page);
                var flags = PermissionTemplateCalculator.FlagsFor(template);
                _db.RolePagePermissions.Add(new RolePagePermission
                {
                    TenantId = tenantId,
                    RoleId = role.Id,
                    PageKey = page,
                    // No access is stored as an all-false row labelled Custom.
                    Template = template ?? PermissionTemplates.Custom,
                    CanCreate = flags.Create,
                    CanRead = flags.Read,
                    CanUpdate = flags.Update,
                    CanDelete = flags.Delete,
                });
                added++;
            }
        }

        if (added > 0) await _db.SaveChangesAsync(ct);
        return added;
    }
}
