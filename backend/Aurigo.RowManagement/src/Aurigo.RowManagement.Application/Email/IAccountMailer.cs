using Aurigo.RowManagement.Domain.Entities;

namespace Aurigo.RowManagement.Application.Email;

/// <summary>
/// Issues an activation/reset token, builds the link and sends the matching email.
/// Returns the link so callers (e.g. an admin UI) can also surface a copyable invite link.
/// A failing email provider is logged and swallowed; the link is still returned.
/// </summary>
public interface IAccountMailer
{
    Task<string> SendInviteAsync(AppUser user, string? roleName, CancellationToken ct = default);
    Task<string> SendResetAsync(AppUser user, CancellationToken ct = default);
}
