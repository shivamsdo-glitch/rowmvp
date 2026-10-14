using Aurigo.RowManagement.Application.Email;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Email;

public class AccountEmailsTests
{
    private const string Link = "http://localhost:5173/activate?token=abc123";

    [Fact]
    public void Invite_contains_the_link_and_role_in_both_bodies()
    {
        var mail = AccountEmails.Invite("ada@row.local", "Ada Lovelace", Link, "Negotiator");

        mail.ToAddress.Should().Be("ada@row.local");
        mail.Subject.Should().Contain("ROW Management");
        mail.TextBody.Should().Contain(Link).And.Contain("Negotiator");
        mail.HtmlBody.Should().Contain(Link).And.Contain("Negotiator");
    }

    [Fact]
    public void Reset_contains_the_link_and_the_one_hour_expiry()
    {
        var mail = AccountEmails.Reset("ada@row.local", "Ada", "http://localhost:5173/reset-password?token=xyz");

        mail.TextBody.Should().Contain("reset-password?token=xyz").And.Contain("1 hour");
    }

    [Fact]
    public void User_supplied_names_are_html_escaped()
    {
        var mail = AccountEmails.Invite("x@row.local", "<script>alert(1)</script>", Link, null);

        mail.HtmlBody.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
    }

    [Fact]
    public void Missing_name_falls_back_to_a_greeting()
    {
        AccountEmails.Reset("x@row.local", null, Link).TextBody.Should().StartWith("Hi there,");
    }
}
