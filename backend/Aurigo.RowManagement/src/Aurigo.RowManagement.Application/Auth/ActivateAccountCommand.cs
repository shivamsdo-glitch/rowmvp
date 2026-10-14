using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Auth;

/// <summary>
/// Shared by account activation and password reset: consume an emailed single-use token, set the
/// password, and sign the user in. Using the token and setting the password commit in one save.
/// </summary>
public sealed class TokenPasswordFlow
{
    private readonly IRowDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;

    public TokenPasswordFlow(IRowDbContext db, ITokenService tokens, IPasswordHasher hasher, IJwtTokenService jwt)
    {
        _db = db;
        _tokens = tokens;
        _hasher = hasher;
        _jwt = jwt;
    }

    public async Task<AuthResponse> RunAsync(string rawToken, string purpose, string newPassword, CancellationToken ct)
    {
        var userId = await _tokens.TryConsumeAsync(rawToken, purpose, ct)
                     ?? throw InvalidLink();

        // Anonymous endpoint: no tenant context, so bypass the tenant filter.
        var user = await _db.AppUsers
            .IgnoreQueryFilters()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw InvalidLink();

        if (user.IsLocked || !user.IsActive)
            throw new AuthenticationFailedException("account_unavailable",
                "This account is locked or inactive. Contact your administrator.");

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == user.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            throw new AuthenticationFailedException("tenant_unavailable",
                "This organization is not available. Contact your administrator.");

        user.PasswordHash = _hasher.Hash(newPassword);
        user.PasswordSetAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct); // persists the used token and the new password together

        return AuthResponseFactory.Create(user, tenant, _jwt);
    }

    private static InvalidRequestException InvalidLink() =>
        new("invalid_token", "This link is invalid or has expired. Request a new one.");
}

/// <summary>First-login activation via an emailed invite token: sets the password and signs the user in.</summary>
public sealed record ActivateAccountCommand(string Token, string Password) : IRequest<AuthResponse>;

public sealed class ActivateAccountCommandValidator : AbstractValidator<ActivateAccountCommand>
{
    public ActivateAccountCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).ValidNewPassword();
    }
}

public sealed class ActivateAccountCommandHandler : IRequestHandler<ActivateAccountCommand, AuthResponse>
{
    private readonly TokenPasswordFlow _flow;
    public ActivateAccountCommandHandler(TokenPasswordFlow flow) => _flow = flow;

    public Task<AuthResponse> Handle(ActivateAccountCommand request, CancellationToken ct) =>
        _flow.RunAsync(request.Token, TokenPurpose.Activation, request.Password, ct);
}
