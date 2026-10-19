using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Domain.Common;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Auth;

public class AuthValidatorTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("1234567", false)]            // 7 chars
    [InlineData("12345678", true)]            // 8 chars: the minimum
    public void Activation_password_minimum_length(string password, bool valid) =>
        new ActivateAccountCommandValidator().Validate(new ActivateAccountCommand("tok", password)).IsValid.Should().Be(valid);

    [Fact]
    public void Activation_password_maximum_length_protects_pbkdf2_from_huge_inputs()
    {
        var validator = new ResetPasswordCommandValidator();
        validator.Validate(new ResetPasswordCommand("tok", new string('x', 128))).IsValid.Should().BeTrue();
        validator.Validate(new ResetPasswordCommand("tok", new string('x', 129))).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("rowdesign", true)]
    [InlineData("user@row.local", true)]
    public void Login_requires_a_username(string email, bool valid) =>
        new LoginCommandValidator().Validate(new LoginCommand(email, "whatever")).IsValid.Should().Be(valid);

    [Fact]
    public void Login_requires_a_password_but_does_not_enforce_the_new_password_minimum()
    {
        var validator = new LoginCommandValidator();
        validator.Validate(new LoginCommand("a@row.local", "")).IsValid.Should().BeFalse();
        validator.Validate(new LoginCommand("a@row.local", "short")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Dev_token_accepts_each_default_role()
    {
        foreach (var role in RoleNames.All)
            new IssueDevTokenQueryValidator().Validate(new IssueDevTokenQuery(new[] { role }, null, null, null))
                .IsValid.Should().BeTrue(role);
    }

    [Fact]
    public void Dev_token_rejects_unknown_or_missing_roles()
    {
        var validator = new IssueDevTokenQueryValidator();
        validator.Validate(new IssueDevTokenQuery(new[] { "SuperUser" }, null, null, null)).IsValid.Should().BeFalse();
        validator.Validate(new IssueDevTokenQuery(Array.Empty<string>(), null, null, null)).IsValid.Should().BeFalse();
        validator.Validate(new IssueDevTokenQuery(new[] { "admin" }, null, null, null)).IsValid.Should().BeFalse(); // case-sensitive
    }

    [Fact]
    public void Role_constants_are_the_eleven_default_roles()
    {
        RoleNames.All.Should().BeEquivalentTo(
            "ProjectManager", "Designer", "DesignApprover", "Surveyor", "TitleResearcher", "Appraiser",
            "AppraisalApprover", "Negotiator", "RelocationAgent", "Finance", "Admin");
    }

    [Fact]
    public void ROW_Phase15_every_default_role_has_exactly_one_demo_sign_in_with_the_agreed_username()
    {
        RoleNames.DemoLogins.Select(l => l.Role).Should().BeEquivalentTo(RoleNames.All);
        RoleNames.DemoLogins.Select(l => l.Username).Should().BeEquivalentTo(
            "rowprojectmanager", "rowdesign", "rowdesignapprover", "surveyor", "titleresearcher", "appraiser",
            "appraisalapprover", "rownegotiator", "relocationagent", "rowfinance", "admin");
    }

    [Theory]
    [InlineData("  User@Row.LOCAL ", "user@row.local")]
    public void Emails_are_normalised_to_trimmed_lower_case(string input, string expected) =>
        AuthValidation.NormalizeEmail(input).Should().Be(expected);
}
