using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Domain.Entities;

namespace Aurigo.RowManagement.Application.Mappers;

/// <summary>Entity to DTO mapping. Entities never leave the Application layer.</summary>
public static class UserMapper
{
    public static IReadOnlyList<string> RoleNames(this AppUser user) =>
        user.UserRoles
            .Select(ur => ur.Role?.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Distinct()
            .ToArray();

    public static UserDto ToDto(this AppUser user, string? tenantName) => new(
        user.Id,
        user.FirstName,
        user.LastName,
        user.FullName,
        Initials(user.FirstName, user.LastName, user.Email),
        user.Email,
        user.RoleNames(),
        user.TenantId,
        tenantName);

    public static string Initials(string? first, string? last, string email)
    {
        var f = string.IsNullOrEmpty(first) ? "" : first[..1];
        var l = string.IsNullOrEmpty(last) ? "" : last[..1];
        var s = (f + l).ToUpperInvariant();
        if (s.Length > 0) return s;
        return email.Length > 0 ? email[..1].ToUpperInvariant() : "U";
    }
}
