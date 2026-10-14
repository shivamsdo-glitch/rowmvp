using Serilog.Core;
using Serilog.Events;

namespace Aurigo.RowManagement.Api.Logging;

/// <summary>
/// Adds <c>TenantId</c>, <c>UserId</c> and <c>RequestId</c> to every log event so sinks can pivot on
/// tenant and user without parsing messages. Properties are only added when a value exists, so
/// bootstrap-time events (no HttpContext) stay uncluttered.
/// </summary>
public sealed class TenantEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _http;
    public TenantEnricher(IHttpContextAccessor http) => _http = http;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var ctx = _http.HttpContext;
        if (ctx is null) return;

        Add(logEvent, propertyFactory, "TenantId", ctx.User.FindFirst("tenant_id")?.Value);
        Add(logEvent, propertyFactory, "UserId", ctx.User.FindFirst("sub")?.Value);
        Add(logEvent, propertyFactory, "RequestId", ctx.TraceIdentifier);
    }

    private static void Add(LogEvent logEvent, ILogEventPropertyFactory factory, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            logEvent.AddPropertyIfAbsent(factory.CreateProperty(name, value));
    }
}
