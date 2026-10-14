namespace Aurigo.RowManagement.Application.Email;

public sealed record EmailMessage(string ToAddress, string? ToName, string Subject, string HtmlBody, string TextBody);

/// <summary>Sends transactional email. Implementations must never log message bodies in production.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
