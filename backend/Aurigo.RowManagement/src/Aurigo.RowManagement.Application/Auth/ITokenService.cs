namespace Aurigo.RowManagement.Application.Auth;

public static class TokenPurpose
{
    public const string Activation = "Activation";
    public const string Reset = "Reset";
}

/// <summary>Issues and consumes single-use, expiring tokens for account activation and password reset.</summary>
public interface ITokenService
{
    /// <summary>
    /// Persists a token for the user and returns the RAW token (only its hash is stored).
    /// The tenant is passed explicitly because this runs on anonymous requests with no tenant context.
    /// </summary>
    Task<string> IssueAsync(Guid tenantId, Guid userId, string purpose, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Validates a raw token and marks it used on the tracked context. Returns the user id, or null
    /// if the token is unknown, expired or already used. The CALLER must SaveChanges so that using the
    /// token and the change it authorises (e.g. setting the password) commit atomically.
    /// </summary>
    Task<Guid?> TryConsumeAsync(string rawToken, string purpose, CancellationToken ct = default);
}
