using Aurigo.RowManagement.Application.Auth;
using Microsoft.AspNetCore.Http;

namespace Aurigo.RowManagement.Infrastructure.Auth;

/// <summary>Resolves the tenant from the JWT <c>tenant_id</c> claim. Empty when the caller is anonymous.</summary>
public sealed class CurrentTenantAccessor : ICurrentTenant
{
    private readonly IHttpContextAccessor _http;
    public CurrentTenantAccessor(IHttpContextAccessor http) => _http = http;

    public Guid Value
    {
        get
        {
            var claim = _http.HttpContext?.User.FindFirst(JwtOptions.TenantClaim)?.Value;
            return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
        }
    }

    public bool IsAvailable => Value != Guid.Empty;
}

/// <summary>Resolves the caller from JWT claims (requires <c>MapInboundClaims = false</c>, so claim names stay as issued).</summary>
public sealed class CurrentUserAccessor : ICurrentUser
{
    private readonly IHttpContextAccessor _http;
    public CurrentUserAccessor(IHttpContextAccessor http) => _http = http;

    private System.Security.Claims.ClaimsPrincipal? Principal => _http.HttpContext?.User;

    public Guid Id => Guid.TryParse(Principal?.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;

    public string Email => Principal?.FindFirst("email")?.Value ?? string.Empty;

    public IReadOnlyList<string> Roles =>
        Principal?.FindAll(JwtOptions.RoleClaim).Select(c => c.Value).ToArray() ?? Array.Empty<string>();

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
}
