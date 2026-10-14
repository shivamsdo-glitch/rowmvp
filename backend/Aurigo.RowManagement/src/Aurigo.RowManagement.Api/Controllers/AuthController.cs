using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aurigo.RowManagement.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IWebHostEnvironment _env;

    public AuthController(IMediator mediator, IWebHostEnvironment env)
    {
        _mediator = mediator;
        _env = env;
    }

    /// <summary>
    /// Development only: issues a JWT for one or more ROW roles with no password. Returns 404 in every
    /// other environment, as if the route did not exist. Roles may be repeated or comma-separated
    /// (<c>?role=Admin&amp;role=Finance</c> or <c>?role=Admin,Finance</c>).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("dev-token")]
    [ProducesResponseType(typeof(DevTokenResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DevToken(
        [FromQuery] string[]? role,
        [FromQuery] Guid? tenant,
        [FromQuery] Guid? user,
        [FromQuery] string? email,
        CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var roles = (role ?? Array.Empty<string>())
            .SelectMany(r => r.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (roles.Length == 0) roles = new[] { RoleNames.Admin };

        return Ok(await _mediator.Send(new IssueDevTokenQuery(roles, tenant, user, email), ct));
    }

    /// <summary>Signs in with email and password.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken ct) =>
        Ok(await _mediator.Send(command, ct));

    /// <summary>First-login activation: sets the password from an emailed invite token and signs the user in.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("activate")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate([FromBody] ActivateAccountCommand command, CancellationToken ct) =>
        Ok(await _mediator.Send(command, ct));

    /// <summary>Sets a new password from an emailed reset token and signs the user in.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command, CancellationToken ct) =>
        Ok(await _mediator.Send(command, ct));

    /// <summary>
    /// Emails an activation or reset link. Always returns 200 with the same message, so it never
    /// reveals whether an address is registered.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordCommand command, CancellationToken ct) =>
        Ok(await _mediator.Send(command, ct));

    /// <summary>
    /// What the caller may do on every page (create/read/update/delete), combined across their roles.
    /// The web app uses it to build navigation and to disable actions the server would refuse.
    /// </summary>
    [Authorize]
    [HttpGet("permissions")]
    [ProducesResponseType(typeof(IReadOnlyList<PagePermissionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Permissions(CancellationToken ct) =>
        Ok(await _mediator.Send(new GetMyPermissionsQuery(), ct));

    /// <summary>The authenticated caller, resolved from the token.</summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        Ok(await _mediator.Send(new GetCurrentUserQuery(), ct));
}
