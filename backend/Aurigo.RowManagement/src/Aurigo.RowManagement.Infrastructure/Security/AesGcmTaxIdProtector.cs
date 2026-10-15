using System.Security.Cryptography;
using System.Text;
using Aurigo.RowManagement.Application.Security;

namespace Aurigo.RowManagement.Infrastructure.Security;

/// <summary>
/// AES-256-GCM. A fresh random nonce for every value, so protecting the same number twice gives different text, and the party's
/// id is authenticated alongside it, so a value moved to another party's record fails to open. Stored as
/// <c>v1.</c> + base64(nonce, tag, cipher text).
/// </summary>
public sealed class AesGcmTaxIdProtector : ITaxIdProtector
{
    private const string Prefix = "v1.";
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] _key;

    public AesGcmTaxIdProtector(TaxIdOptions options) => _key = options.Key;

    public string Protect(string digits, Guid partyId)
    {
        var plain = Encoding.UTF8.GetBytes(digits);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];
        using (var aes = new AesGcm(_key, TagBytes)) aes.Encrypt(nonce, plain, cipher, tag, Associated(partyId));
        CryptographicOperations.ZeroMemory(plain);

        var packed = new byte[NonceBytes + TagBytes + cipher.Length];
        nonce.CopyTo(packed, 0);
        tag.CopyTo(packed, NonceBytes);
        cipher.CopyTo(packed, NonceBytes + TagBytes);
        return Prefix + Convert.ToBase64String(packed);
    }

    public string Unprotect(string protectedValue, Guid partyId)
    {
        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal)) throw new CryptographicException("Unknown tax number format.");
        var packed = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        if (packed.Length < NonceBytes + TagBytes) throw new CryptographicException("The stored tax number is damaged.");

        var nonce = packed.AsSpan(0, NonceBytes);
        var tag = packed.AsSpan(NonceBytes, TagBytes);
        var cipher = packed.AsSpan(NonceBytes + TagBytes);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(_key, TagBytes)) aes.Decrypt(nonce, cipher, tag, plain, Associated(partyId));
        var text = Encoding.UTF8.GetString(plain);
        CryptographicOperations.ZeroMemory(plain);
        return text;
    }

    private static byte[] Associated(Guid partyId) => Encoding.UTF8.GetBytes($"row-tax-id:{partyId:N}");
}
