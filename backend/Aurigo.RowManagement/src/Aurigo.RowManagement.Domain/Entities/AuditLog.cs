using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// One row per entity create/delete (full snapshot as JSON) and one row per changed field on
/// update. Written only by the auditing SaveChanges interceptor; never by handlers.
/// Tenant-owned but not an <see cref="EntityBase"/>, so it is never audited itself.
/// </summary>
public class AuditLog : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public AuditAction Action { get; set; }
    public string EntityType { get; set; } = default!;
    public Guid EntityId { get; set; }

    /// <summary>Changed property for updates; null for create/delete snapshots.</summary>
    public string? Field { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public Guid ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}
