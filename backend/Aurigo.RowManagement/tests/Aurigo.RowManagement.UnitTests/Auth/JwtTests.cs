using System.IdentityModel.Tokens.Jwt;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Aurigo.RowManagement.UnitTests.Auth;

public class JwtTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        SigningKey = new string('k', 48),
        ExpiryMinutes = 30,
    };

    [Fact]
    public void Issued_token_carries_subject_email_tenant_and_one_claim_per_role()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var issued = new JwtTokenService(Options).Issue(
            new JwtSubject(userId, tenantId, "pm@row.local", new[] { RoleNames.ProjectManager, RoleNames.Finance }));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);
        jwt.Issuer.Should().Be("test-issuer");
        jwt.Audiences.Should().ContainSingle("test-audience");
        jwt.Subject.Should().Be(userId.ToString());
        jwt.Claims.Single(c => c.Type == "email").Value.Should().Be("pm@row.local");
        jwt.Claims.Single(c => c.Type == "tenant_id").Value.Should().Be(tenantId.ToString());
        jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value)
            .Should().BeEquivalentTo(RoleNames.ProjectManager, RoleNames.Finance);
    }

    [Fact]
    public void Token_expires_after_the_configured_minutes()
    {
        var issued = new JwtTokenService(Options).Issue(
            new JwtSubject(Guid.NewGuid(), Guid.NewGuid(), "a@row.local", new[] { RoleNames.Admin }));

        issued.ExpiresAtUtc.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(30), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Resolve_outside_development_requires_a_signing_key()
    {
        var act = () => JwtOptions.Resolve(Config(), Env(Environments.Production));
        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:SigningKey*");
    }

    [Fact]
    public void Resolve_in_development_falls_back_to_a_dev_only_key()
    {
        var options = JwtOptions.Resolve(Config(), Env(Environments.Development));
        options.SigningKey.Length.Should().BeGreaterOrEqualTo(32);
    }

    [Fact]
    public void Resolve_rejects_a_key_shorter_than_32_bytes_even_in_development()
    {
        var act = () => JwtOptions.Resolve(Config(("Jwt:SigningKey", "too-short")), Env(Environments.Development));
        act.Should().Throw<InvalidOperationException>().WithMessage("*32 bytes*");
    }

    [Fact]
    public void Resolve_uses_configured_values()
    {
        var options = JwtOptions.Resolve(
            Config(("Jwt:SigningKey", new string('z', 40)), ("Jwt:Issuer", "me"), ("Jwt:ExpiryMinutes", "15")),
            Env(Environments.Production));

        options.Issuer.Should().Be("me");
        options.ExpiryMinutes.Should().Be(15);
        options.SigningKey.Should().Be(new string('z', 40));
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private static IHostEnvironment Env(string name) => new FakeEnv(name);
}
