using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>Swagger, compression, CORS and rate limiting.</summary>
public class PlatformBehaviourTests
{
    [Fact]
    public async Task Swagger_is_served_in_development_with_versioned_routes_and_bearer_auth()
    {
        await using var factory = new RowApiFactory();
        var json = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");

        using var doc = JsonDocument.Parse(json);
        var paths = doc.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        paths.Should().Contain(new[]
        {
            "/api/v1/auth/login", "/api/v1/auth/dev-token", "/api/v1/auth/activate",
            "/api/v1/auth/reset-password", "/api/v1/auth/forgot-password", "/api/v1/auth/me",
            "/api/v1/auth/permissions", "/api/v1/documents", "/api/v1/documents/{id}/content", "/api/v1/saved-filters",
        });
        paths.Should().OnlyContain(p => p.StartsWith("/api/v1/"));
        doc.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Swagger_is_off_in_production_unless_explicitly_enabled()
    {
        var settings = new Dictionary<string, string?> { ["Jwt:SigningKey"] = new string('p', 48), ["Security:TaxIdKey"] = Support.TestKeys.TaxIdKey };
        await using var off = new RowApiFactory("Production", settings);
        (await off.CreateClient().GetAsync("/swagger/v1/swagger.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var on = new RowApiFactory("Production", new(settings) { ["Swagger:Enabled"] = "true" });
        (await on.CreateClient().GetAsync("/swagger/v1/swagger.json")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Production_refuses_to_start_without_a_jwt_signing_key()
    {
        await using var factory = new RowApiFactory("Production");

        var act = () => factory.CreateClient().GetAsync("/health");

        (await act.Should().ThrowAsync<Exception>()).And.ToString().Should().Contain("Jwt:SigningKey");
    }

    [Theory]
    [InlineData("br", "br")]
    [InlineData("gzip", "gzip")]
    public async Task Responses_are_compressed_with_brotli_or_gzip(string acceptEncoding, string expected)
    {
        await using var factory = new RowApiFactory();
        var request = new HttpRequestMessage(HttpMethod.Get, "/swagger/v1/swagger.json");
        request.Headers.AcceptEncoding.ParseAdd(acceptEncoding);

        var response = await factory.CreateClient().SendAsync(request);

        response.Content.Headers.ContentEncoding.Should().Contain(expected);
    }

    [Fact]
    public async Task Cors_allows_the_vite_dev_origin_and_not_others()
    {
        await using var factory = new RowApiFactory();
        var client = factory.CreateClient();

        HttpRequestMessage Preflight(string origin)
        {
            var r = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
            r.Headers.Add("Origin", origin);
            r.Headers.Add("Access-Control-Request-Method", "POST");
            return r;
        }

        var allowed = await client.SendAsync(Preflight("http://localhost:5173"));
        allowed.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("http://localhost:5173");

        var denied = await client.SendAsync(Preflight("https://evil.example"));
        denied.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task The_anonymous_auth_endpoints_are_rate_limited_with_a_problem_json_429()
    {
        await using var factory = RowApiFactory.WithSettings(new() { ["RateLimiting:AuthPermitLimit"] = "3" });
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "a@row.local", password = "wrong-pass-1" }))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var limited = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "a@row.local", password = "wrong-pass-1" });

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        limited.Headers.Contains("Retry-After").Should().BeTrue();
        (await limited.Content.ReadAsStringAsync()).Should().Contain("rate_limited");
    }

    [Fact]
    public async Task Writes_are_limited_per_user_but_reads_are_not()
    {
        await using var factory = RowApiFactory.WithSettings(new() { ["RateLimiting:WritePermitLimit"] = "2" });
        var admin = factory.ClientWithToken(await factory.DevTokenAsync(factory.CreateClient(), "Admin"));

        // Reads: unlimited.
        for (var i = 0; i < 10; i++)
            (await admin.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Writes: the third in the window is refused.
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await admin.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "x@row.local" })).StatusCode);

        statuses.Should().Equal(HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task One_users_write_budget_does_not_consume_another_users()
    {
        await using var factory = RowApiFactory.WithSettings(new() { ["RateLimiting:WritePermitLimit"] = "1" });
        var anon = factory.CreateClient();
        var a = factory.ClientWithToken(await factory.DevTokenAsync(anon, "Admin", user: Guid.NewGuid()));
        var b = factory.ClientWithToken(await factory.DevTokenAsync(anon, "Admin", user: Guid.NewGuid()));
        var body = new { email = "x@row.local" };

        (await a.PostAsJsonAsync("/api/v1/auth/forgot-password", body)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await a.PostAsJsonAsync("/api/v1/auth/forgot-password", body)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await b.PostAsJsonAsync("/api/v1/auth/forgot-password", body)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
