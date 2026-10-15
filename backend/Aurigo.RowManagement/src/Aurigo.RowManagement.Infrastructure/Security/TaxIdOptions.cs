using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Aurigo.RowManagement.Infrastructure.Security;

/// <summary>The key that protects tax identification numbers at rest (ROW-018).</summary>
public sealed class TaxIdOptions
{
    public const string SectionName = "Security";
    public const int KeyBytes = 32; // AES-256

    /// <summary>The 32-byte key.</summary>
    public byte[] Key { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Resolves the key from <c>Security:TaxIdKey</c> (base64 of 32 random bytes, for example from <c>openssl rand -base64 32</c>).
    /// It must be supplied everywhere except Development, where a fixed dev-only key is used. Losing the key makes stored tax
    /// numbers unreadable, so it is held in a secret store and backed up with the database.
    /// </summary>
    public static TaxIdOptions Resolve(IConfiguration config, IHostEnvironment env)
    {
        var value = config.GetSection(SectionName)["TaxIdKey"];
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!env.IsDevelopment())
                throw new InvalidOperationException(
                    "Security:TaxIdKey must be configured (environment variable Security__TaxIdKey or a secrets manager) outside Development. " +
                    "Use base64 of 32 random bytes.");
            value = DevelopmentKey;
        }

        byte[] key;
        try { key = Convert.FromBase64String(value.Trim()); }
        catch (FormatException) { throw new InvalidOperationException("Security:TaxIdKey must be base64 text."); }
        if (key.Length != KeyBytes) throw new InvalidOperationException($"Security:TaxIdKey must decode to exactly {KeyBytes} bytes.");
        return new TaxIdOptions { Key = key };
    }

    /// <summary>Development only. Never use for real data.</summary>
    private const string DevelopmentKey = "REVWX09OTFlfcm93X3RheF9pZF9rZXlfMzJieXRlcyE=";
}
