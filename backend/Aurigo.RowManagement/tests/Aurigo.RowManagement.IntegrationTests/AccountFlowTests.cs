using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>Activation, login, forgot and reset password end to end, against the seeded users.</summary>
public class AccountFlowTests
{
    private const string Email = "negotiator@row.local";
    private const string Password1 = "first-Password-1";
    private const string Password2 = "second-Password-2";

    private static async Task<JsonDocument> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync());

    [Fact]
    public async Task Full_lifecycle_invite_activate_login_forgot_and_reset()
    {
        await using var factory = new RowApiFactory();
        var client = factory.CreateClient();

        // 1. Not activated yet: login is told to activate (409), not "wrong password".
        var early = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = "whatever1" });
        early.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(early)).RootElement.GetProperty("title").GetString().Should().Be("activation_required");

        // 2. Forgot-password on a not-yet-activated account sends an INVITE with an activation link.
        (await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = Email })).EnsureSuccessStatusCode();
        var invite = factory.Emails.Sent.Single(m => m.ToAddress == Email);
        invite.Subject.Should().Contain("invited");
        invite.TextBody.Should().Contain("/activate?token=").And.Contain("Negotiator");
        var activationToken = factory.Emails.LastTokenFor(Email);

        // 3. A weak password is a validation error and does NOT burn the token.
        var weak = await client.PostAsJsonAsync("/api/v1/auth/activate", new { token = activationToken, password = "short" });
        weak.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 4. Activate: sets the password and signs the user in.
        var activate = await client.PostAsJsonAsync("/api/v1/auth/activate", new { token = activationToken, password = Password1 });
        activate.StatusCode.Should().Be(HttpStatusCode.OK);
        var signedIn = await activate.Content.ReadFromJsonAsync<AuthResponse>(RowApiFactory.JsonOptions);
        signedIn!.User.Roles.Should().Equal(RoleNames.Negotiator);
        signedIn.User.TenantId.Should().Be(WellKnownIds.DevTenant);
        signedIn.TokenType.Should().Be("Bearer");

        // The token from activation really works.
        var me = await factory.ClientWithToken(signedIn.Token).GetAsync("/api/v1/auth/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. The link is single-use.
        var reuse = await client.PostAsJsonAsync("/api/v1/auth/activate", new { token = activationToken, password = Password2 });
        reuse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(reuse)).RootElement.GetProperty("title").GetString().Should().Be("invalid_token");

        // 6. Login now works; wrong password does not; email is case-insensitive.
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email.ToUpperInvariant(), password = Password1 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = "nope-nope-nope" });
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 7. Forgot-password on an ACTIVATED account sends a RESET link.
        (await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = Email })).EnsureSuccessStatusCode();
        factory.Emails.Sent.Last(m => m.ToAddress == Email).Subject.Should().Contain("Reset");
        var resetToken = factory.Emails.LastTokenFor(Email);
        resetToken.Should().NotBe(activationToken);

        // 8. A reset token cannot be used as an activation token (purpose is checked).
        (await client.PostAsJsonAsync("/api/v1/auth/activate", new { token = resetToken, password = Password2 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 9. Reset: new password works, old one stops working.
        (await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = resetToken, password = Password2 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password2 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password1 }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_are_indistinguishable()
    {
        await using var factory = new RowApiFactory();
        var client = factory.CreateClient();
        await ActivateAsync(factory, client);

        var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = "definitely-wrong" });
        var unknownEmail = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@row.local", password = "definitely-wrong" });

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var a = (await Json(wrongPassword)).RootElement;
        var b = (await Json(unknownEmail)).RootElement;
        a.GetProperty("title").GetString().Should().Be(b.GetProperty("title").GetString());
        a.GetProperty("detail").GetString().Should().Be(b.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Forgot_password_never_reveals_whether_an_email_is_registered()
    {
        await using var factory = new RowApiFactory();
        var client = factory.CreateClient();

        var known = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = Email });
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "nobody@row.local" });

        known.StatusCode.Should().Be(HttpStatusCode.OK);
        unknown.StatusCode.Should().Be(HttpStatusCode.OK);
        (await known.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync());
        factory.Emails.Sent.Should().ContainSingle().Which.ToAddress.Should().Be(Email);
    }

    [Fact]
    public async Task A_locked_account_gets_no_email_and_cannot_log_in()
    {
        await using var factory = new RowApiFactory();
        var client = factory.CreateClient();
        await ActivateAsync(factory, client);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();
            db.CurrentTenantId = WellKnownIds.DevTenant;
            (await db.AppUsers.SingleAsync(u => u.Email == Email)).IsLocked = true;
            await db.SaveChangesAsync();
        }
        var sentBefore = factory.Emails.Sent.Count;

        await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = Email });
        factory.Emails.Sent.Count.Should().Be(sentBefore);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password1 });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Json(login)).RootElement.GetProperty("title").GetString().Should().Be("account_unavailable");
    }

    [Fact]
    public async Task A_deactivated_tenant_blocks_login()
    {
        await using var factory = new RowApiFactory();
        var client = factory.CreateClient();
        await ActivateAsync(factory, client);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();
            (await db.Tenants.SingleAsync()).IsActive = false;
            await db.SaveChangesAsync();
        }

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password1 });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Json(login)).RootElement.GetProperty("title").GetString().Should().Be("tenant_unavailable");
    }

    [Fact]
    public async Task Validation_errors_are_a_400_problem_json_with_field_errors_and_a_trace_id()
    {
        await using var factory = new RowApiFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email = "", password = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var root = (await Json(response)).RootElement;
        root.GetProperty("title").GetString().Should().Be("validation_failed");
        root.GetProperty("errors").EnumerateObject().Select(p => p.Name).Should().Contain(new[] { "Email", "Password" });
        root.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_unexpected_exception_becomes_a_generic_500_that_leaks_nothing()
    {
        await using var factory = new RowApiFactory();
        var broken = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.Remove(services.Single(d => d.ServiceType == typeof(IPasswordHasher)));
            services.AddSingleton<IPasswordHasher, ExplodingHasher>();
        }));

        var response = await broken.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = "ghost@row.local", password = "irrelevant-1" });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("internal_error").And.Contain("traceId");
        body.Should().NotContain("SECRET-INTERNAL-DETAIL").And.NotContain("ExplodingHasher").And.NotContain("at Aurigo");
    }

    [Fact]
    public async Task Activation_is_audited_with_the_password_hash_redacted_and_the_tenant_stamped()
    {
        await using var factory = new RowApiFactory();
        var client = factory.CreateClient();
        await ActivateAsync(factory, client);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();
        var logs = await db.AuditLogs.IgnoreQueryFilters().Where(a => a.EntityType != nameof(Tenant)).ToListAsync();

        // The anonymous token issue wrote a UserToken; the audit row carries the user's tenant, not Guid.Empty.
        logs.Should().Contain(a => a.EntityType == nameof(UserToken) && a.Action == AuditAction.Create
                                   && a.TenantId == WellKnownIds.DevTenant);
        var passwordChange = logs.Single(a => a.EntityType == nameof(AppUser) && a.Field == nameof(AppUser.PasswordHash));
        passwordChange.Action.Should().Be(AuditAction.Update);
        passwordChange.OldValue.Should().Be("[redacted]");
        passwordChange.NewValue.Should().Be("[redacted]");
        logs.Should().OnlyContain(a => a.TenantId != Guid.Empty);
        var all = string.Join("|", logs.SelectMany(a => new[] { a.OldValue, a.NewValue }));
        all.Should().NotContain(Password1);
    }

    private static async Task ActivateAsync(RowApiFactory factory, HttpClient client)
    {
        await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = Email });
        var token = factory.Emails.LastTokenFor(Email);
        (await client.PostAsJsonAsync("/api/v1/auth/activate", new { token, password = Password1 })).EnsureSuccessStatusCode();
    }

    private sealed class ExplodingHasher : IPasswordHasher
    {
        public string Hash(string password) => throw new InvalidOperationException("SECRET-INTERNAL-DETAIL");
        public bool Verify(string password, string storedHash) => throw new InvalidOperationException("SECRET-INTERNAL-DETAIL");
    }
}
