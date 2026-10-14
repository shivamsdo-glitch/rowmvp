using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Domain.Entities;

public class AppUser : EntityBase
{
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; }

    /// <summary>PBKDF2 hash ("iterations.salt.hash", base64). Null means provisioned but not yet activated.</summary>
    public string? PasswordHash { get; set; }
    public DateTime? PasswordSetAt { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

public class Role : EntityBase
{
    /// <summary>One of <see cref="RoleNames.All"/>.</summary>
    public string Name { get; set; } = default!;
    public string? Description { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePagePermission> Permissions { get; set; } = new List<RolePagePermission>();
}

public class UserRole : EntityBase
{
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
}
