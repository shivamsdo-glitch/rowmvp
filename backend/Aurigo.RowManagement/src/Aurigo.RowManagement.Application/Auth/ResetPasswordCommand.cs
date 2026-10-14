using FluentValidation;
using MediatR;

namespace Aurigo.RowManagement.Application.Auth;

/// <summary>Sets a new password from an emailed reset token and signs the user in.</summary>
public sealed record ResetPasswordCommand(string Token, string Password) : IRequest<AuthResponse>;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).ValidNewPassword();
    }
}

public sealed class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, AuthResponse>
{
    private readonly TokenPasswordFlow _flow;
    public ResetPasswordCommandHandler(TokenPasswordFlow flow) => _flow = flow;

    public Task<AuthResponse> Handle(ResetPasswordCommand request, CancellationToken ct) =>
        _flow.RunAsync(request.Token, TokenPurpose.Reset, request.Password, ct);
}
