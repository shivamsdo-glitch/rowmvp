using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// One row per (role, page key): what members of the role may do on that page. A row with every
/// flag false means "no access" and is kept so every role lists every page in an editor.
/// </summary>
public class RolePagePermission : EntityBase
{
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }

    /// <summary>One of <see cref="PageKeys.All"/>.</summary>
    public string PageKey { get; set; } = default!;

    /// <summary>One of <see cref="PermissionTemplates.All"/>; a label only, the flags below are authoritative.</summary>
    public string Template { get; set; } = PermissionTemplates.Custom;

    public bool CanCreate { get; set; }
    public bool CanRead { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
}
