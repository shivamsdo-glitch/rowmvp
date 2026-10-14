using FluentValidation;

namespace Aurigo.RowManagement.Application.Auth;

/// <summary>Shared FluentValidation rules for the auth requests.</summary>
public static class AuthValidation
{
    public const int PasswordMinLength = 8;

    /// <summary>Upper bound so a huge password cannot be used to burn CPU in PBKDF2.</summary>
    public const int PasswordMaxLength = 128;

    public const int EmailMaxLength = 254;

    public static IRuleBuilderOptions<T, string> ValidNewPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(PasswordMinLength).WithMessage($"Password must be at least {PasswordMinLength} characters.")
            .MaximumLength(PasswordMaxLength).WithMessage($"Password must be at most {PasswordMaxLength} characters.");

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MaximumLength(EmailMaxLength)
            .EmailAddress().WithMessage("Enter a valid email address.");

    /// <summary>Emails are stored and compared lower-cased and trimmed.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
