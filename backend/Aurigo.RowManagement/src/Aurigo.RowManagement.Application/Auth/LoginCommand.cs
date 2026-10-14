using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Auth;

public sealed record LoginCommand(string Email, string Password) : IRequest<AuthResponse>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(AuthValidation.PasswordMaxLength);
    }
}

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResponse>
{
    // A well-formed hash that matches nothing. Verified against when the email is unknown so that
    // "no such user" costs the same PBKDF2 work as "wrong password" (no timing oracle for emails).
    // Iteration count mirrors Pbkdf2PasswordHasher.
    private const string DummyHash =
        "100000.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private readonly IRowDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;

    public LoginCommandHandler(IRowDbContext db, IPasswordHasher hasher, IJwtTokenService jwt)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
    }

    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken ct)
    {
        var email = AuthValidation.NormalizeEmail(request.Email);

        // Anonymous endpoint: there is no tenant context yet, so the tenant filter is bypassed.
        // Email is globally unique, so the email alone identifies the tenant.
        var user = await _db.AppUsers
            .IgnoreQueryFilters()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            _hasher.Verify(request.Password, DummyHash);
            throw InvalidCredentials();
        }

        if (string.IsNullOrEmpty(user.PasswordHash))
            throw new ConflictException("activation_required",
                "This account hasn't been set up yet. Set a password to activate it.");

        // Check the password before revealing anything about account state.
        if (!_hasher.Verify(request.Password, user.PasswordHash))
            throw InvalidCredentials();

        if (user.IsLocked || !user.IsActive)
            throw new AuthenticationFailedException("account_unavailable",
                "This account is locked or inactive. Contact your administrator.");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == user.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            throw new AuthenticationFailedException("tenant_unavailable",
                "This organization is not available. Contact your administrator.");

        return AuthResponseFactory.Create(user, tenant, _jwt);
    }

    private static AuthenticationFailedException InvalidCredentials() =>
        new("invalid_credentials", "Incorrect email or password.");
}
