namespace Aurigo.RowManagement.Domain.Common;

/// <summary>Marks an entity as belonging to a tenant. Every such type gets the EF global tenant filter.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
