using Aurigo.RowManagement.Application.ExternalServices;
using Microsoft.Extensions.Logging;

namespace Aurigo.RowManagement.Infrastructure.ExternalClients;

/// <summary>Stand-in for a future notification service: logs what would have been sent.</summary>
public sealed class StubNotificationClient : INotificationClient
{
    private readonly ILogger<StubNotificationClient> _log;
    public StubNotificationClient(ILogger<StubNotificationClient> log) => _log = log;

    public Task SendAsync(NotificationRequest request, CancellationToken ct)
    {
        _log.LogInformation("[StubNotification] {Channel} to {RecipientCount} recipient(s): {Subject}",
            request.Channel, request.Recipients.Count, request.Subject);
        return Task.CompletedTask;
    }
}
