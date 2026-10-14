namespace Aurigo.RowManagement.Application.Auth;

public sealed record JwtSubject(Guid UserId, Guid TenantId, string Email, IReadOnlyList<string> Roles);

public sealed record IssuedToken(string Token, DateTime ExpiresAtUtc);

/// <summary>Issues this application's own signed JWTs (no external identity provider).</summary>
public interface IJwtTokenService
{
    IssuedToken Issue(JwtSubject subject);
}
