using Aurigo.RowManagement.Application.Email;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aurigo.RowManagement.Infrastructure.Email;

/// <summary>
/// The only <see cref="IEmailSender"/> in the MVP: writes the email to the log instead of sending it.
/// In Development the plain-text body is logged because it contains the activation/reset link; in any
/// other environment only the recipient and subject are logged, so live links never reach the logs.
/// </summary>
public sealed class ConsoleEmailSender : IEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _log;
    private readonly IHostEnvironment _env;

    public ConsoleEmailSender(ILogger<ConsoleEmailSender> log, IHostEnvironment env)
    {
        _log = log;
        _env = env;
    }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (_env.IsDevelopment())
            _log.LogInformation("[Email:Console] To <{To}> | {Subject}\n{Body}",
                message.ToAddress, message.Subject, message.TextBody);
        else
            _log.LogInformation("[Email:Console] To <{To}> | {Subject} (body not logged outside Development)",
                message.ToAddress, message.Subject);
        return Task.CompletedTask;
    }
}
