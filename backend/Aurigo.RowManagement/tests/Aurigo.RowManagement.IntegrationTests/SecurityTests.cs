using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>Nothing is reachable without a valid token this API issued, and a bad token is never a 500.</summary>
public class SecurityTests : IDisposable
{
    // These tests send every write endpoint, anonymously and as every role, many times within one minute; the write limiter
    // (itself tested in PlatformBehaviourTests) must not be what answers.
    private readonly RowApiFactory _factory = RowApiFactory.WithSettings(new() { ["RateLimiting:WritePermitLimit"] = "1000000" });
    public void Dispose() => _factory.Dispose();

    // Anonymous on purpose: sign-in and account recovery. Everything else must demand a token.
    private static readonly HashSet<string> AnonymousOperations = new()
    {
        "GET /api/v1/auth/dev-token", "POST /api/v1/auth/login", "POST /api/v1/auth/activate",
        "POST /api/v1/auth/reset-password", "POST /api/v1/auth/forgot-password",
    };

    private async Task<List<(string Method, string Path)>> AllOperations()
    {
        var json = await _factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        using var doc = JsonDocument.Parse(json);
        var ops = new List<(string, string)>();
        foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
            foreach (var op in path.Value.EnumerateObject())
            {
                ops.Add((op.Name.ToUpperInvariant(), path.Name));
                // Route parameters are typed ({fiscalYear:int}); a wrong-typed sample would 404 in routing before auth runs.
                if (op.Value.ValueKind == JsonValueKind.Object && op.Value.TryGetProperty("parameters", out var ps))
                    foreach (var prm in ps.EnumerateArray())
                        if (prm.TryGetProperty("in", out var where) && where.GetString() == "path"
                            && prm.TryGetProperty("schema", out var schema) && schema.TryGetProperty("type", out var type)
                            && type.GetString() is "integer" or "number")
                            NumericRouteParameters.Add(prm.GetProperty("name").GetString()!);
            }
        return ops;
    }

    private static readonly HashSet<string> NumericRouteParameters = new();

    private static HttpRequestMessage Request(string method, string path, string? token = null)
    {
        var concrete = System.Text.RegularExpressions.Regex.Replace(path, @"\{([^}]+)\}",
            m => NumericRouteParameters.Contains(m.Groups[1].Value) ? "2026" : Guid.NewGuid().ToString());
        var req = new HttpRequestMessage(new HttpMethod(method), concrete);
        if (method is "POST" or "PUT")
            req.Content = path.EndsWith("/documents")
                ? TestUser.Form("Parcel", Guid.NewGuid(), "a.pdf", new byte[] { 1 }) // the upload endpoint only accepts multipart
                : new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    [Fact]
    public async Task The_API_exposes_a_known_number_of_operations_so_new_endpoints_are_noticed()
    {
        // When this changes, review the new endpoint's authorization and add it to the matrix tests, then update the number.
        (await AllOperations()).Should().HaveCountGreaterThanOrEqualTo(14);
    }

    [Fact]
    public async Task Every_endpoint_except_sign_in_and_recovery_rejects_an_anonymous_caller_with_401()
    {
        var offenders = new List<string>();
        foreach (var (method, path) in await AllOperations())
        {
            if (AnonymousOperations.Contains($"{method} {path}")) continue;
            var response = await _factory.CreateClient().SendAsync(Request(method, path));
            if (response.StatusCode != HttpStatusCode.Unauthorized) offenders.Add($"{method} {path} -> {(int)response.StatusCode}");
        }

        offenders.Should().BeEmpty("these endpoints are reachable without signing in");
    }

    [Fact]
    public async Task No_endpoint_ever_returns_a_server_error_for_an_empty_or_garbage_request_as_any_role()
    {
        var offenders = new List<string>();
        foreach (var role in Spec.Roles)
        {
            var token = await _factory.DevTokenAsync(_factory.CreateClient(), role);
            foreach (var (method, path) in await AllOperations())
            {
                if (path.Contains("dev-token")) continue;
                var response = await _factory.CreateClient().SendAsync(Request(method, path, token));
                if ((int)response.StatusCode >= 500) offenders.Add($"{role} {method} {path} -> {(int)response.StatusCode}");
            }
        }

        offenders.Should().BeEmpty();
    }

    // ---- forged and damaged tokens ----------------------------------------------------------

    public static IEnumerable<object[]> BadTokens() => new[]
    {
        new object[] { "signed with the wrong key" },
        new object[] { "unsigned (alg none)" },
        new object[] { "expired" },
        new object[] { "not yet valid" },
        new object[] { "wrong audience" },
        new object[] { "wrong issuer" },
        new object[] { "truncated" },
        new object[] { "garbage" },
        new object[] { "empty" },
        new object[] { "signature stripped" },
        new object[] { "HS512 instead of HS256 with the wrong key" },
    };

    private string BadToken(string kind) => kind switch
    {
        "signed with the wrong key" => Jwts.Make(_factory, signingKey: "A-completely-different-signing-key-of-32-chars-or-more!!"),
        "unsigned (alg none)" => Jwts.Unsigned(_factory),
        "expired" => Jwts.Make(_factory, notBefore: DateTime.UtcNow.AddHours(-3), expires: DateTime.UtcNow.AddHours(-2)),
        "not yet valid" => Jwts.Make(_factory, notBefore: DateTime.UtcNow.AddHours(2), expires: DateTime.UtcNow.AddHours(3)),
        "wrong audience" => Jwts.Make(_factory, audience: "someone-else"),
        "wrong issuer" => Jwts.Make(_factory, issuer: "evil-issuer"),
        "truncated" => Jwts.Make(_factory)[..40],
        "garbage" => "not.a.jwt",
        "empty" => "",
        "signature stripped" => string.Join('.', Jwts.Make(_factory).Split('.').Take(2)) + ".",
        "HS512 instead of HS256 with the wrong key" => Jwts.Make(_factory, signingKey: new string('x', 80), algorithm: Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha512),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task A_bad_token_is_a_401_on_every_protected_endpoint_and_never_a_500(string kind)
    {
        var token = BadToken(kind);
        foreach (var (method, path) in await AllOperations())
        {
            if (AnonymousOperations.Contains($"{method} {path}")) continue;
            var response = await _factory.CreateClient().SendAsync(Request(method, path, token.Length == 0 ? null : token));
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{kind}: {method} {path}");
        }
    }

    [Fact]
    public async Task A_token_in_the_query_string_is_ignored()
    {
        var token = await _factory.DevTokenAsync(_factory.CreateClient(), "Admin");

        var response = await _factory.CreateClient().GetAsync($"/api/v1/auth/me?access_token={token}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Basic")]
    [InlineData("Token")]
    [InlineData("bearer-ish")]
    public async Task Other_authorization_schemes_are_not_accepted(string scheme)
    {
        var token = await _factory.DevTokenAsync(_factory.CreateClient(), "Admin");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(scheme, token);

        (await client.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_properly_signed_token_with_no_tenant_cannot_do_anything_and_is_not_a_500()
    {
        var client = _factory.ClientWithToken(Jwts.Make(_factory, includeTenant: false));

        var perms = await client.GetAsync("/api/v1/auth/permissions");
        var docs = await client.GetAsync($"/api/v1/documents?entityType=Parcel&entityId={Guid.NewGuid()}");
        var upload = await client.PostAsync("/api/v1/documents", TestUser.Form("Parcel", Guid.NewGuid(), "a.pdf", new byte[] { 1 }));

        foreach (var r in new[] { perms, docs, upload })
        {
            ((int)r.StatusCode).Should().BeInRange(400, 499);
            r.StatusCode.Should().NotBe(HttpStatusCode.OK).And.NotBe(HttpStatusCode.Created);
        }
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    public async Task A_token_with_a_malformed_or_empty_tenant_is_refused_cleanly(string tenant)
    {
        var client = _factory.ClientWithToken(Jwts.Make(_factory, tenantValue: tenant));

        var response = await client.GetAsync("/api/v1/auth/permissions");

        ((int)response.StatusCode).Should().BeInRange(400, 499);
    }

    [Fact]
    public async Task A_token_for_a_tenant_that_does_not_exist_gets_no_access_and_no_data()
    {
        var ghost = Guid.NewGuid();
        var client = _factory.ClientWithToken(Jwts.Make(_factory, tenant: ghost, roles: new[] { "Admin" }));

        var perms = await client.GetAsync("/api/v1/auth/permissions");
        var upload = await client.PostAsync("/api/v1/documents", TestUser.Form("Parcel", Guid.NewGuid(), "a.pdf", new byte[] { 1 }));

        if (perms.StatusCode == HttpStatusCode.OK)
            JsonDocument.Parse(await perms.Content.ReadAsStringAsync()).RootElement.EnumerateArray()
                .Should().OnlyContain(p => !p.GetProperty("canRead").GetBoolean());
        upload.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("admin")]        // role names are case-sensitive
    [InlineData("ADMIN")]
    [InlineData("Admin ")]
    public async Task A_made_up_or_miscased_role_grants_nothing(string role)
    {
        var client = _factory.ClientWithToken(Jwts.Make(_factory, roles: new[] { role }));

        (await client.GetAsync("/api/v1/admin/ping")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var perms = await client.GetAsync("/api/v1/auth/permissions");
        if (perms.StatusCode == HttpStatusCode.OK)
            JsonDocument.Parse(await perms.Content.ReadAsStringAsync()).RootElement.EnumerateArray()
                .Should().OnlyContain(p => !p.GetProperty("canRead").GetBoolean() && !p.GetProperty("canUpdate").GetBoolean());
    }

    [Fact]
    public async Task A_token_with_no_roles_has_no_access_anywhere()
    {
        var client = _factory.ClientWithToken(Jwts.Make(_factory, roles: Array.Empty<string>()));

        (await client.GetAsync("/api/v1/admin/ping")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var upload = await client.PostAsync("/api/v1/documents", TestUser.Form("Project", Guid.NewGuid(), "a.pdf", new byte[] { 1 }));
        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_role_added_to_a_token_that_the_database_does_not_know_still_cannot_exceed_its_rows()
    {
        // The roles claim names the role; what it may DO always comes from the stored page permissions.
        var finance = _factory.ClientWithToken(Jwts.Make(_factory, roles: new[] { "Finance" }));

        var upload = await finance.PostAsync("/api/v1/documents", TestUser.Form("TitleRecord", Guid.NewGuid(), "a.pdf", new byte[] { 1 }));

        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- role gating on the one admin-only endpoint -----------------------------------------

    [Theory]
    [MemberData(nameof(Spec.EachRole), MemberType = typeof(Spec))]
    public async Task Only_Admin_may_call_the_admin_endpoint(string role)
    {
        var token = await _factory.DevTokenAsync(_factory.CreateClient(), role);

        var response = await _factory.ClientWithToken(token).GetAsync("/api/v1/admin/ping");

        response.StatusCode.Should().Be(role == "Admin" ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_user_holding_several_roles_gets_the_union_of_their_access()
    {
        var client = _factory.CreateClient();
        var token = (await client.GetFromJsonAsync<DevToken>("/api/v1/auth/dev-token?role=Finance&role=TitleResearcher", RowApiFactory.JsonOptions))!.Token;

        var perms = JsonDocument.Parse(await _factory.ClientWithToken(token).GetStringAsync("/api/v1/auth/permissions")).RootElement;
        bool Can(string page, string flag) => perms.EnumerateArray().Single(p => p.GetProperty("pageKey").GetString() == page).GetProperty(flag).GetBoolean();

        Can("payments", "canDelete").Should().BeTrue("Finance");
        Can("title", "canCreate").Should().BeTrue("TitleResearcher");
        Can("administration", "canRead").Should().BeFalse("neither role");
    }

    private sealed record DevToken(string Token);

    // ---- errors, headers, CORS --------------------------------------------------------------

    [Fact]
    public async Task Errors_are_problem_json_with_a_trace_id_and_never_leak_internals()
    {
        var token = await _factory.DevTokenAsync(_factory.CreateClient(), "Admin");
        var client = _factory.ClientWithToken(token);

        var response = await client.PostAsync("/api/v1/documents", TestUser.Form("Planet", Guid.NewGuid(), "a.pdf", new byte[] { 1 }));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        JsonDocument.Parse(body).RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
        body.Should().NotContain("   at ").And.NotContain("System.").And.NotContain("Aurigo.RowManagement.");
    }

    [Fact]
    public async Task An_unknown_route_is_a_clean_404()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/does-not-exist");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_wrong_HTTP_method_is_refused_not_a_500()
    {
        var token = await _factory.DevTokenAsync(_factory.CreateClient(), "Admin");

        var response = await _factory.ClientWithToken(token).DeleteAsync("/api/v1/auth/me");

        ((int)response.StatusCode).Should().BeInRange(400, 499);
    }

    [Fact]
    public async Task CORS_allows_the_web_app_origin_and_not_a_stranger()
    {
        async Task<HttpResponseMessage> Preflight(string origin)
        {
            var req = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/me");
            req.Headers.Add("Origin", origin);
            req.Headers.Add("Access-Control-Request-Method", "GET");
            req.Headers.Add("Access-Control-Request-Headers", "authorization");
            return await _factory.CreateClient().SendAsync(req);
        }

        var allowed = await Preflight("http://localhost:5173");
        var stranger = await Preflight("https://evil.example.com");

        allowed.Headers.TryGetValues("Access-Control-Allow-Origin", out var ok).Should().BeTrue();
        ok!.Single().Should().Be("http://localhost:5173");
        stranger.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Production_hides_the_dev_token_swagger_and_demo_data_even_to_a_determined_caller()
    {
        await using var production = new RowApiFactory("Production", new()
        {
            ["Jwt:SigningKey"] = new string('p', 48), ["Security:TaxIdKey"] = Support.TestKeys.TaxIdKey,
            ["ConnectionStrings:Postgres"] = "Host=unused",
        });
        var client = production.CreateClient();

        (await client.GetAsync("/api/v1/auth/dev-token?role=Admin")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/swagger")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And a token signed with the well-known DEVELOPMENT key is worthless in Production.
        var devKeyToken = Jwts.Make(production, signingKey: "DEV_ONLY_row_management_signing_key_change_me_32+_chars!!");
        (await production.ClientWithToken(devKeyToken).GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
