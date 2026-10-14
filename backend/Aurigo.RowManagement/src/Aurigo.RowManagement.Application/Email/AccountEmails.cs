namespace Aurigo.RowManagement.Application.Email;

/// <summary>Builds the invite (activation) and password-reset emails.</summary>
public static class AccountEmails
{
    private const string Product = "ROW Management";

    public static EmailMessage Invite(string toAddress, string? toName, string link, string? roleName)
    {
        var name = string.IsNullOrWhiteSpace(toName) ? "there" : toName;
        var roleLine = string.IsNullOrWhiteSpace(roleName) ? "" : $" Your role: {roleName}.";
        var subject = $"You've been invited to {Product}";
        var text =
            $"Hi {name},\n\n" +
            $"An administrator has created a {Product} account for you.{roleLine}\n\n" +
            $"Set your password to activate your account:\n{link}\n\n" +
            "This link expires in 7 days. If you weren't expecting this, you can ignore this email.";
        var html = Wrap(
            $"<h2 style=\"margin:0 0 12px;color:#0f172a\">Welcome to {Product}</h2>" +
            $"<p>Hi {Esc(name)},</p>" +
            $"<p>An administrator has created a {Product} account for you." +
            (string.IsNullOrWhiteSpace(roleName) ? "" : $" Your role is <strong>{Esc(roleName!)}</strong>.") + "</p>" +
            "<p>Click below to set your password and activate your account:</p>" +
            Button("Create your password", link) +
            "<p style=\"color:#64748b;font-size:13px\">This link expires in 7 days. If you weren't expecting this, you can ignore this email.</p>");
        return new EmailMessage(toAddress, toName, subject, html, text);
    }

    public static EmailMessage Reset(string toAddress, string? toName, string link)
    {
        var name = string.IsNullOrWhiteSpace(toName) ? "there" : toName;
        var subject = $"Reset your {Product} password";
        var text =
            $"Hi {name},\n\n" +
            $"We received a request to reset your {Product} password.\n\n" +
            $"Reset it here:\n{link}\n\n" +
            "This link expires in 1 hour. If you didn't request this, you can ignore this email.";
        var html = Wrap(
            "<h2 style=\"margin:0 0 12px;color:#0f172a\">Reset your password</h2>" +
            $"<p>Hi {Esc(name)},</p>" +
            $"<p>We received a request to reset your {Product} password.</p>" +
            Button("Reset password", link) +
            "<p style=\"color:#64748b;font-size:13px\">This link expires in 1 hour. If you didn't request this, you can ignore this email.</p>");
        return new EmailMessage(toAddress, toName, subject, html, text);
    }

    private static string Button(string label, string href) =>
        $"<p style=\"margin:24px 0\"><a href=\"{Esc(href)}\" style=\"background:#0f6e6e;color:#fff;text-decoration:none;" +
        $"padding:12px 22px;border-radius:6px;display:inline-block;font-weight:600\">{Esc(label)}</a></p>" +
        $"<p style=\"color:#64748b;font-size:12px;word-break:break-all\">Or paste this link: {Esc(href)}</p>";

    private static string Wrap(string inner) =>
        "<div style=\"font-family:system-ui,Segoe UI,Arial,sans-serif;max-width:520px;margin:0 auto;" +
        "padding:24px;color:#0f172a;line-height:1.55\">" + inner + "</div>";

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
