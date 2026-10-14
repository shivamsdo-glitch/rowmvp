using System.Security.Cryptography;
using System.Text;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Infrastructure.Auth;

/// <summary>
/// Single-use token service. The raw token is a 256-bit URL-safe random string returned to the caller
/// (for the email link); only its SHA-256 hash is persisted.
/// </summary>
public sealed class OneTimeTokenService : ITokenService
{
    private readonly IRowDbContext _db;
    public OneTimeTokenService(IRowDbContext db) => _db = db;

    public async Task<string> IssueAsync(Guid tenantId, Guid userId, string purpose, TimeSpan ttl, CancellationToken ct = default)
    {
        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        _db.UserTokens.Add(new UserToken
        {
            TenantId = tenantId,
            UserId = userId,
            Purpose = purpose,
            TokenHash = Sha256(raw),
            ExpiresAt = DateTime.UtcNow.Add(ttl),
        });
        await _db.SaveChangesAsync(ct);
        return raw;
    }

    public async Task<Guid?> TryConsumeAsync(string rawToken, string purpose, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;
        var hash = Sha256(rawToken);

        // Activation and reset run anonymously, with no tenant context. The 256-bit token hash is the
        // secret, so a global lookup is correct and intended: bypass the tenant filter.
        var token = await _db.UserTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, ct);
        if (token is null || token.UsedAt is not null || token.ExpiresAt < DateTime.UtcNow) return null;

        token.UsedAt = DateTime.UtcNow; // persisted by the caller's SaveChanges
        return token.UserId;
    }

    private static string Sha256(string input) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
