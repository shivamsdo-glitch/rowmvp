using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// Single-use, expiring token for account activation or password reset. Only the SHA-256 hash is
/// stored; the raw token exists only in the emailed link.
/// </summary>
public class UserToken : EntityBase
{
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = string.Empty;   // "Activation" | "Reset"
    public string TokenHash { get; set; } = string.Empty; // base64 SHA-256 of the raw token
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
