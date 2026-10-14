using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aurigo.RowManagement.Api.RateLimiting;

/// <summary>Bound from the <c>RateLimiting</c> configuration section.</summary>
public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting";
    public const string AuthPolicy = "auth";

    /// <summary>Writes (POST/PUT/PATCH/DELETE) allowed per user per window. Reads are not limited.</summary>
    public int WritePermitLimit { get; set; } = 120;

    /// <summary>Requests per IP per window to the anonymous auth endpoints (credential-stuffing defence).</summary>
    public int AuthPermitLimit { get; set; } = 10;

    public int WindowSeconds { get; set; } = 60;
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddRowRateLimiting(this IServiceCollection services)
    {
        // Settings resolve lazily from IConfiguration so late-supplied configuration is honoured.
        services.AddOptions<RateLimitSettings>()
            .Configure<IConfiguration>((s, cfg) => cfg.GetSection(RateLimitSettings.SectionName).Bind(s));
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<Microsoft.Extensions.Options.IOptions<RateLimitSettings>>((o, s) => Configure(o, s.Value));
        return services;
    }

    private static void Configure(RateLimiterOptions options, RateLimitSettings s)
    {
        var window = TimeSpan.FromSeconds(Math.Max(1, s.WindowSeconds));
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Basic limit on write endpoints only: one window per authenticated user (IP when anonymous).
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        {
            if (!IsWrite(http.Request.Method))
                return RateLimitPartition.GetNoLimiter("reads");

            var key = http.User.FindFirst("sub")?.Value
                      ?? http.Connection.RemoteIpAddress?.ToString()
                      ?? "anonymous";
            return RateLimitPartition.GetFixedWindowLimiter($"write:{key}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = s.WritePermitLimit,
                Window = window,
                QueueLimit = 0,
            });
        });

        options.AddPolicy(RateLimitSettings.AuthPolicy, http =>
            RateLimitPartition.GetFixedWindowLimiter(
                $"auth:{http.Connection.RemoteIpAddress?.ToString() ?? "anonymous"}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = s.AuthPermitLimit,
                    Window = window,
                    QueueLimit = 0,
                }));

        options.OnRejected = async (context, ct) =>
        {
            var http = context.HttpContext;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                http.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();

            http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            http.Response.ContentType = "application/problem+json";
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "rate_limited",
                Detail = "Too many requests. Try again shortly.",
                Type = "urn:aurigo:row:problem:rate_limited",
            };
            problem.Extensions["traceId"] = http.TraceIdentifier;
            await http.Response.WriteAsync(
                JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ct);
        };
    }

    private static bool IsWrite(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
}
