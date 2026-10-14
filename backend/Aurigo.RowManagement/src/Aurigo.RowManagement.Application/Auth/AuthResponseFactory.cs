using Aurigo.RowManagement.Application.Mappers;
using Aurigo.RowManagement.Domain.Entities;

namespace Aurigo.RowManagement.Application.Auth;

internal static class AuthResponseFactory
{
    public static AuthResponse Create(AppUser user, Tenant tenant, IJwtTokenService jwt)
    {
        var dto = user.ToDto(tenant.Name);
        var issued = jwt.Issue(new JwtSubject(user.Id, user.TenantId, user.Email, dto.Roles));
        return new AuthResponse(issued.Token, "Bearer", issued.ExpiresAtUtc, dto);
    }
}
