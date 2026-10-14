using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Aurigo.RowManagement.Infrastructure.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Aurigo.RowManagement.IntegrationTests.Support;

/// <summary>Hand-built tokens, valid or deliberately broken, to prove the API trusts only what it issued.</summary>
public static class Jwts
{
    public static JwtOptions Options(RowApiFactory factory) => factory.Services.GetRequiredService<JwtOptions>();

    public static string Make(
        RowApiFactory factory,
        string[]? roles = null,
        Guid? tenant = null,
        string? subject = null,
        string? signingKey = null,
        string? issuer = null,
        string? audience = null,
        DateTime? notBefore = null,
        DateTime? expires = null,
        bool includeTenant = true,
        string? tenantValue = null,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var o = Options(factory);
        var claims = new List<Claim>
        {
            new("sub", subject ?? Guid.NewGuid().ToString()),
            new("email", "forged@example.com"),
            new("jti", Guid.NewGuid().ToString()),
        };
        if (includeTenant) claims.Add(new Claim(JwtOptions.TenantClaim, tenantValue ?? (tenant ?? Domain.WellKnown.DevTenant).ToString()));
        foreach (var r in roles ?? new[] { "Admin" }) claims.Add(new Claim(JwtOptions.RoleClaim, r));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? o.SigningKey));
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer ?? o.Issuer, audience ?? o.Audience, claims,
            notBefore ?? now.AddMinutes(-1), expires ?? now.AddMinutes(30),
            new SigningCredentials(key, algorithm));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>An unsigned token ("alg":"none") carrying admin claims: the classic bypass attempt.</summary>
    public static string Unsigned(RowApiFactory factory)
    {
        var o = Options(factory);
        string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var payload = B64($"{{\"sub\":\"{Guid.NewGuid()}\",\"iss\":\"{o.Issuer}\",\"aud\":\"{o.Audience}\",\"exp\":{exp},\"tenant_id\":\"{Domain.WellKnown.DevTenant}\",\"role\":\"Admin\"}}");
        return $"{header}.{payload}.";
    }
}

internal static class Domain
{
    public static class WellKnown
    {
        public static readonly Guid DevTenant = new("00000000-0000-0000-0000-000000000001");
    }
}
