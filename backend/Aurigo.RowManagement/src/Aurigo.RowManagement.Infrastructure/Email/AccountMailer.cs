using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Email;
using Aurigo.RowManagement.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aurigo.RowManagement.Infrastructure.Email;

public sealed class AccountMailer : IAccountMailer
{
    private static readonly TimeSpan InviteTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan ResetTtl = TimeSpan.FromHours(1);

    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly ILogger<AccountMailer> _log;
    private readonly string _publicUrl;

    public AccountMailer(ITokenService tokens, IEmailSender email, IConfiguration config, ILogger<AccountMailer> log)
    {
        _tokens = tokens;
        _email = email;
        _log = log;
        _publicUrl = (config["App:PublicUrl"] ?? "http://localhost:5173").TrimEnd('/');
    }

    public Task<string> SendInviteAsync(AppUser user, string? roleName, CancellationToken ct = default) =>
        IssueAndSendAsync(user, TokenPurpose.Activation, InviteTtl, "activate",
            link => AccountEmails.Invite(user.Email, user.FullName, link, roleName), ct);

    public Task<string> SendResetAsync(AppUser user, CancellationToken ct = default) =>
        IssueAndSendAsync(user, TokenPurpose.Reset, ResetTtl, "reset-password",
            link => AccountEmails.Reset(user.Email, user.FullName, link), ct);

    // Issues the token and builds the link first, then attempts the send. The link is always
    // returned (even if the email fails) so an admin can copy and share it manually.
    private async Task<string> IssueAndSendAsync(
        AppUser user, string purpose, TimeSpan ttl, string path,
        Func<string, EmailMessage> build, CancellationToken ct)
    {
        var raw = await _tokens.IssueAsync(user.TenantId, user.Id, purpose, ttl, ct);
        var link = $"{_publicUrl}/{path}?token={raw}";
        try
        {
            await _email.SendAsync(build(link), ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Email send failed for user {UserId} ({Purpose}); link still issued.", user.Id, purpose);
        }
        return link;
    }
}
