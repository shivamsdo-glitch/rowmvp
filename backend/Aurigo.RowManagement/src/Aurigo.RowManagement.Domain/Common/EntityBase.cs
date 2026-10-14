namespace Aurigo.RowManagement.Domain.Common;

/// <summary>
/// Base for every tenant-owned aggregate root and child entity. TenantId is stamped and the
/// audit columns are maintained by the auditing SaveChanges interceptor, not by handlers.
/// </summary>
public abstract class EntityBase : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedBy { get; set; }
}
