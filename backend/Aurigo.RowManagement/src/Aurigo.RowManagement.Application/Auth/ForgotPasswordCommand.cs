using Aurigo.RowManagement.Application.Email;
using Aurigo.RowManagement.Application.Mappers;
using Aurigo.RowManagement.Application.Persistence;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Auth;

/// <summary>
/// Requests an email with an activation or reset link. Always succeeds with the same message so it
/// never reveals whether an email is registered. Accounts that were never activated get a fresh
/// invite; activated accounts get a reset link.
/// </summary>
public sealed record ForgotPasswordCommand(string Email) : IRequest<MessageResponse>;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator() => RuleFor(x => x.Email).ValidEmail();
}

public sealed class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, MessageResponse>
{
    public const string ResponseMessage = "If an account exists for that email, a link has been sent.";

    private readonly IRowDbContext _db;
    private readonly IAccountMailer _mailer;

    public ForgotPasswordCommandHandler(IRowDbContext db, IAccountMailer mailer)
    {
        _db = db;
        _mailer = mailer;
    }

    public async Task<MessageResponse> Handle(ForgotPasswordCommand request, CancellationToken ct)
    {
        var email = AuthValidation.NormalizeEmail(request.Email);

        var user = await _db.AppUsers
            .IgnoreQueryFilters() // anonymous endpoint: no tenant context
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is { IsActive: true, IsLocked: false })
        {
            var tenantActive = await _db.Tenants.AnyAsync(t => t.Id == user.TenantId && t.IsActive, ct);
            if (tenantActive)
            {
                if (string.IsNullOrEmpty(user.PasswordHash))
                    await _mailer.SendInviteAsync(user, user.RoleNames().FirstOrDefault(), ct);
                else
                    await _mailer.SendResetAsync(user, ct);
            }
        }

        return new MessageResponse(ResponseMessage);
    }
}
