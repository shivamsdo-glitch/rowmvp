namespace Aurigo.RowManagement.Application.ExternalServices;

/// <summary>
/// Future external notification service (vault ROW-170/171 alerts, reminders, mass messaging).
/// No real client exists in the MVP: only <c>StubNotificationClient</c> is registered.
/// </summary>
public interface INotificationClient
{
    Task SendAsync(NotificationRequest request, CancellationToken ct);
}

public sealed record NotificationRequest(
    string Channel, // email | sms | inapp
    IReadOnlyList<string> Recipients,
    string Subject,
    string Body,
    IReadOnlyDictionary<string, string>? Metadata = null);
