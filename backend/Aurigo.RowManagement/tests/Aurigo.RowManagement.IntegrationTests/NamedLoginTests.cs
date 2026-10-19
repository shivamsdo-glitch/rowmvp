using System.Net;
using System.Net.Http.Json;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Domain.Common;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>Phase 1.5 slice 1: the eleven named demo sign-ins and username-based login ("Next requirements", Landing page).</summary>
public class NamedLoginTests
{
    private const string DemoPassword = "qwertyuiop";

    public static IEnumerable<object[]> Logins() =>
        RoleNames.DemoLogins.Select(l => new object[] { l.Username, l.Role });

    [Theory]
    [MemberData(nameof(Logins))]
    public async Task ROW_Phase15_each_named_user_signs_in_with_the_shared_password_and_gets_exactly_its_role(string username, string role)
    {
        await using var factory = new RowApiFactory();
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username, password = DemoPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>(RowApiFactory.JsonOptions);
        body!.User.Roles.Should().Equal(role);
        var me = await factory.ClientWithToken(body.Token).GetAsync("/api/v1/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ROW_Phase15_username_is_case_insensitive_and_the_email_still_works()
    {
        await using var factory = new RowApiFactory();
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "  RowDesign ", password = DemoPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "rowdesign@row.local", password = DemoPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ROW_Phase15_a_wrong_password_or_unknown_username_is_the_same_401()
    {
        await using var factory = new RowApiFactory();
        var client = factory.CreateClient();

        var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "not-the-password" });
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "nobody", password = DemoPassword });

        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        static async Task<string> Detail(HttpResponseMessage r) =>
            System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString()!;
        (await Detail(wrong)).Should().Be(await Detail(unknown));
    }

    [Fact]
    public async Task ROW_Phase15_the_demo_password_is_never_seeded_outside_Development()
    {
        await using var factory = new RowApiFactory("Production", new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = new string('k', 48),
            ["Security:TaxIdKey"] = Convert.ToBase64String(new byte[32]),
            ["Database:SeedOnStartup"] = "true",
        });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = DemoPassword });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
