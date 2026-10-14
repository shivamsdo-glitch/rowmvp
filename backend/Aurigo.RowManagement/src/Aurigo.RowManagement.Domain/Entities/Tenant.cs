namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// An agency (DOT, city or county). Deliberately NOT tenant-owned: it is the tenant, so it is
/// not subject to the tenant query filter and is looked up during anonymous login.
/// </summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
