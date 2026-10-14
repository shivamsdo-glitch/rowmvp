namespace Aurigo.RowManagement.Domain.Entities;

public static class WellKnownIds
{
    /// <summary>The single tenant used by the MVP demo and by dev tokens.</summary>
    public static readonly Guid DevTenant = new("00000000-0000-0000-0000-000000000001");

    /// <summary>Default subject of a dev token when none is supplied.</summary>
    public static readonly Guid DevUser = new("00000000-0000-0000-0000-000000000099");
}
