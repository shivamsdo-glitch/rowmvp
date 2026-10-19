using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aurigo.RowManagement.IntegrationTests;

public class DevTokenAndRolesTests : IClassFixture<RowApiFactory>
{
    private readonly RowApiFactory _factory;
    public DevTokenAndRolesTests(RowApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("ProjectManager")]
    [InlineData("Designer")]
    [InlineData("DesignApprover")]
    [InlineData("Surveyor")]
    [InlineData("TitleResearcher")]
    [InlineData("Appraiser")]
    [InlineData("AppraisalApprover")]
    [InlineData("Negotiator")]
    [InlineData("RelocationAgent")]
    [InlineData("Finance")]
    [InlineData("Admin")]
    public async Task Dev_token_works_for_each_default_role(string role)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/auth/dev-token?role={role}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DevTokenResponse>(RowApiFactory.JsonOptions);
        body!.Roles.Should().Equal(role);
        body.TenantId.Should().Be(WellKnownIds.DevTenant);
        body.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Dev_token_accepts_several_roles_repeated_or_comma_separated()
    {
        var client = _factory.CreateClient();

        var repeated = await client.GetFromJsonAsync<DevTokenResponse>(
            "/api/v1/auth/dev-token?role=Admin&role=Finance", RowApiFactory.JsonOptions);
        var commas = await client.GetFromJsonAsync<DevTokenResponse>(
            "/api/v1/auth/dev-token?role=Admin,Finance", RowApiFactory.JsonOptions);

        repeated!.Roles.Should().BeEquivalentTo("Admin", "Finance");
        commas!.Roles.Should().BeEquivalentTo("Admin", "Finance");
    }

    [Fact]
    public async Task Dev_token_rejects_an_unknown_role_with_a_validation_problem()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/auth/dev-token?role=SuperUser");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Dev_token_does_not_exist_outside_development()
    {
        await using var production = new RowApiFactory("Production", new()
        {
            ["Jwt:SigningKey"] = new string('p', 48), ["Security:TaxIdKey"] = Support.TestKeys.TaxIdKey,
            ["ConnectionStrings:Postgres"] = "Host=unused",
        });

        var response = await production.CreateClient().GetAsync("/api/v1/auth/dev-token?role=Admin");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Protected_endpoints_return_401_without_a_token()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/admin/ping")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_garbage_or_wrongly_signed_token_is_rejected()
    {
        (await _factory.ClientWithToken("not.a.jwt").GetAsync("/api/v1/auth/me")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        // Validly shaped but signed with a different key.
        await using var other = new RowApiFactory("Development", new() { ["Jwt:SigningKey"] = new string('x', 48) });
        var foreign = await other.DevTokenAsync(other.CreateClient(), RoleNames.Admin);
        (await _factory.ClientWithToken(foreign).GetAsync("/api/v1/admin/ping")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Role_gate_returns_403_for_the_wrong_role_and_200_for_Admin()
    {
        var anon = _factory.CreateClient();

        var appraiser = _factory.ClientWithToken(await _factory.DevTokenAsync(anon, RoleNames.Appraiser));
        (await appraiser.GetAsync("/api/v1/admin/ping")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = _factory.ClientWithToken(await _factory.DevTokenAsync(anon, RoleNames.Admin));
        (await admin.GetAsync("/api/v1/admin/ping")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_user_with_several_roles_passes_any_gate_they_hold()
    {
        var anon = _factory.CreateClient();
        var response = await anon.GetFromJsonAsync<DevTokenResponse>(
            "/api/v1/auth/dev-token?role=Finance,Admin", RowApiFactory.JsonOptions);

        var client = _factory.ClientWithToken(response!.Token);
        (await client.GetAsync("/api/v1/admin/ping")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Me_describes_the_caller_from_their_token()
    {
        var anon = _factory.CreateClient();
        var client = _factory.ClientWithToken(await _factory.DevTokenAsync(anon, RoleNames.Negotiator));

        var me = await client.GetFromJsonAsync<UserDto>("/api/v1/auth/me", RowApiFactory.JsonOptions);

        me!.Roles.Should().Equal(RoleNames.Negotiator);
        me.TenantId.Should().Be(WellKnownIds.DevTenant);
        me.TenantName.Should().Be("Demo Agency (Development)");
    }

    [Fact]
    public async Task Me_never_resolves_a_user_that_belongs_to_another_tenant()
    {
        // The seeded Negotiator exists in the dev tenant. A token for ANOTHER tenant that claims the
        // same user id must not see that user's name: the tenant filter hides the row.
        Guid seededUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Aurigo.RowManagement.Infrastructure.Persistence.RowDbContext>();
            seededUserId = db.AppUsers.IgnoreQueryFilters().Single(u => u.Email == "negotiator@row.local").Id;
        }

        var anon = _factory.CreateClient();
        var otherTenantToken = await _factory.DevTokenAsync(anon, RoleNames.Negotiator,
            tenant: Guid.Parse("99999999-0000-0000-0000-000000000009"), user: seededUserId);
        var me = await _factory.ClientWithToken(otherTenantToken)
            .GetFromJsonAsync<UserDto>("/api/v1/auth/me", RowApiFactory.JsonOptions);

        me!.FirstName.Should().BeEmpty();
        me.LastName.Should().BeEmpty();
        me.TenantName.Should().BeNull();

        // And the same user id inside its own tenant does resolve.
        var ownTenantToken = await _factory.DevTokenAsync(anon, RoleNames.Negotiator, user: seededUserId);
        var mine = await _factory.ClientWithToken(ownTenantToken)
            .GetFromJsonAsync<UserDto>("/api/v1/auth/me", RowApiFactory.JsonOptions);
        mine!.FirstName.Should().Be("Negotiator");
    }

    [Fact]
    public async Task Health_endpoint_reports_the_database()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public void The_seed_creates_the_dev_tenant_the_eleven_roles_and_the_eleven_named_sign_ins()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Aurigo.RowManagement.Infrastructure.Persistence.RowDbContext>();

        db.Tenants.Should().ContainSingle(t => t.Id == WellKnownIds.DevTenant);
        db.Roles.IgnoreQueryFilters().Select(r => r.Name).Should().BeEquivalentTo(RoleNames.All);
        var named = db.AppUsers.IgnoreQueryFilters().Where(u => u.PasswordHash != null).Select(u => u.Username).ToList();
        named.Should().BeEquivalentTo(RoleNames.DemoLogins.Select(l => l.Username));
    }

    [Fact]
    public async Task Unknown_json_body_properties_do_not_break_binding_and_bad_json_is_a_400()
    {
        var client = _factory.CreateClient();
        var bad = await client.PostAsync("/api/v1/auth/login",
            new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
