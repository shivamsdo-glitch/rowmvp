using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using FluentValidation;
using MediatR;

namespace Aurigo.RowManagement.Application.Auth;

/// <summary>
/// Issues a JWT for one or more ROW roles without a password. The API only exposes this in the
/// Development environment (it returns 404 anywhere else); the handler itself has no environment gate.
/// </summary>
public sealed record IssueDevTokenQuery(
    IReadOnlyList<string> Roles,
    Guid? Tenant,
    Guid? User,
    string? Email) : IRequest<DevTokenResponse>;

public sealed class IssueDevTokenQueryValidator : AbstractValidator<IssueDevTokenQuery>
{
    public IssueDevTokenQueryValidator()
    {
        RuleFor(x => x.Roles).NotEmpty().WithMessage("At least one role is required.");
        RuleForEach(x => x.Roles)
            .Must(RoleNames.IsKnown)
            .WithMessage($"Role must be one of: {string.Join(", ", RoleNames.All)}.");
        RuleFor(x => x.Email!).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public sealed class IssueDevTokenQueryHandler : IRequestHandler<IssueDevTokenQuery, DevTokenResponse>
{
    private readonly IJwtTokenService _jwt;
    public IssueDevTokenQueryHandler(IJwtTokenService jwt) => _jwt = jwt;

    private static Guid DevUserFor(IEnumerable<string> roles) =>
        new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("dev-user:" + string.Join(",", roles.Order(StringComparer.Ordinal))))[..16]);

    public Task<DevTokenResponse> Handle(IssueDevTokenQuery request, CancellationToken ct)
    {
        var tenant = request.Tenant ?? WellKnownIds.DevTenant;
        // One stable person per role, so two people signed in as different roles are different people (the two-person approval rules
        // need that); asking for a user id still wins.
        var user = request.User ?? DevUserFor(request.Roles);
        var email = string.IsNullOrWhiteSpace(request.Email)
            ? $"dev.{request.Roles[0].ToLowerInvariant()}@row.local"
            : AuthValidation.NormalizeEmail(request.Email);

        var issued = _jwt.Issue(new JwtSubject(user, tenant, email, request.Roles));
        return Task.FromResult(new DevTokenResponse(
            issued.Token, "Bearer", issued.ExpiresAtUtc, request.Roles, tenant, user));
    }
}
