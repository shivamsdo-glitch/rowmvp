using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Aurigo.RowManagement.Infrastructure.Auth;

/// <summary>Settings for the JWTs this application issues and validates itself (no external identity provider).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Claim carrying the tenant id. Read by <see cref="CurrentTenantAccessor"/>.</summary>
    public const string TenantClaim = "tenant_id";

    /// <summary>Claim carrying each role name (one claim per role).</summary>
    public const string RoleClaim = "role";

    public string Issuer { get; init; } = "aurigo-row-management";
    public string Audience { get; init; } = "aurigo-row-management-web";
    public string SigningKey { get; init; } = string.Empty;
    public int ExpiryMinutes { get; init; } = 480;

    /// <summary>
    /// Resolves the options from configuration. The signing key must be supplied by environment
    /// variable or secret store everywhere except Development, where a fixed dev-only key is used.
    /// </summary>
    public static JwtOptions Resolve(IConfiguration config, IHostEnvironment env)
    {
        var section = config.GetSection(SectionName);
        var key = section["SigningKey"];

        if (string.IsNullOrWhiteSpace(key))
        {
            if (!env.IsDevelopment())
                throw new InvalidOperationException(
                    "Jwt:SigningKey must be configured (environment variable Jwt__SigningKey or a secrets manager) outside Development.");
            key = "DEV_ONLY_row_management_signing_key_change_me_32+_chars!!";
        }

        if (Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");

        var defaults = new JwtOptions();
        return new JwtOptions
        {
            Issuer = section["Issuer"] ?? defaults.Issuer,
            Audience = section["Audience"] ?? defaults.Audience,
            SigningKey = key,
            ExpiryMinutes = int.TryParse(section["ExpiryMinutes"], out var m) && m > 0 ? m : defaults.ExpiryMinutes,
        };
    }
}
