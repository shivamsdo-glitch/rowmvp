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
            var role = new Role { TenantId = tenantId, Name = name, Description = DefaultRolePermissions.DescriptionFor(name), IsSystem = true };
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
                    Template = PermissionTemplateCalculator.TemplateFor(flags),
                    CanCreate = flags.Create,
                    CanRead = flags.Read,
                    CanUpdate = flags.Update,
                    CanDelete = flags.Delete,
                });
                added++;
            }
        }

        if (added > 0) await _db.SaveChangesAsync(ct);

        await EnsureParcelRulesAsync(tenantId, ct);
        await EnsureLibraryAsync(tenantId, ct);
        return added;
    }

    /// <summary>
    /// Adds the default Library fields and their values an agency is missing. Add-only: a field or value an administrator has
    /// edited, switched off or added is never touched, and a value they removed from use is not brought back.
    /// </summary>
    public async Task EnsureLibraryAsync(Guid tenantId, CancellationToken ct = default)
    {
        var existing = await _db.LibraryFields.IgnoreQueryFilters().Include(f => f.Values).Where(f => f.TenantId == tenantId).ToListAsync(ct);
        var have = existing.Select(f => f.Key).ToList();
        var order = 0;
        var changed = false;
        foreach (var d in DefaultLibrary.Fields)
        {
            order++;
            if (have.Contains(d.Key))
            {
                // A default list that was created empty (earlier versions shipped many lists with no values) gets its starter values. Values are
                // never deleted, only switched off, so a list with no rows at all was never configured by anyone: nothing of theirs is touched.
                var current = existing.First(f => f.Key == d.Key);
                if (current.IsSystem && current.FieldType == d.Type && current.Values.Count == 0 && d.Values.Count > 0)
                {
                    var next = 0;
                    foreach (var v in d.Values)
                        _db.LibraryValues.Add(new LibraryValue { TenantId = tenantId, LibraryFieldId = current.Id, Value = v, NormalizedValue = v.ToUpperInvariant(), SortOrder = next++ });
                    changed = true;
                }
                continue;
            }
            var field = new LibraryField { TenantId = tenantId, Key = d.Key, Label = d.Label, FieldType = d.Type, IsSystem = true, SortOrder = order };
            var sort = 0;
            foreach (var v in d.Values)
                field.Values.Add(new LibraryValue { TenantId = tenantId, LibraryFieldId = field.Id, Value = v, NormalizedValue = v.ToUpperInvariant(), SortOrder = sort++ });
            _db.LibraryFields.Add(field);
            changed = true;
        }
        if (changed) await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Adds the default parcel statuses, workflow templates, placeholder deadline rules and step-gate settings an agency is
    /// missing. Add-only: a status list or rule set an administrator has already edited is never touched.
    /// </summary>
    public async Task EnsureParcelRulesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var changed = false;

        if (!await _db.ParcelStatuses.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenantId, ct))
        {
            var order = 0;
            foreach (var d in DefaultParcelRules.Statuses)
                _db.ParcelStatuses.Add(new ParcelStatus { TenantId = tenantId, Name = d.Name, Color = d.Color, IsInitial = d.IsInitial, SortOrder = order++ });
            changed = true;
        }

        var haveTypes = await _db.WorkflowTemplates.IgnoreQueryFilters().Where(t => t.TenantId == tenantId).Select(t => t.AcquisitionType).ToListAsync(ct);
        foreach (var (type, steps) in DefaultParcelRules.Workflows.Where(w => !haveTypes.Contains(w.Key)))
        {
            var template = new WorkflowTemplate { TenantId = tenantId, AcquisitionType = type };
            var order = 0;
            foreach (var step in steps)
                template.Steps.Add(new WorkflowStep { TenantId = tenantId, TemplateId = template.Id, Key = step.Key, Label = step.Label, Order = order++, RequiresAcquisitionGate = step.RequiresAcquisitionGate });
            _db.WorkflowTemplates.Add(template);
            changed = true;
        }

        if (!await _db.DeadlineRules.IgnoreQueryFilters().AnyAsync(r => r.TenantId == tenantId, ct))
        {
            foreach (var d in DefaultParcelRules.DeadlineRules)
                _db.DeadlineRules.Add(new DeadlineRule { TenantId = tenantId, Name = d.Name, TriggerMilestone = d.Trigger, TargetMilestone = d.Target, Days = d.Days, IsPlaceholder = true });
            changed = true;
        }

        if (!await _db.StepGateSettings.IgnoreQueryFilters().AnyAsync(s => s.TenantId == tenantId, ct))
        {
            _db.StepGateSettings.Add(new StepGateSettings { TenantId = tenantId });
            changed = true;
        }

        if (changed) await _db.SaveChangesAsync(ct);
    }
}
