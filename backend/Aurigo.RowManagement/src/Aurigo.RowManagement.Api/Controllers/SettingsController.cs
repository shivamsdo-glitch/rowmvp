using Aurigo.RowManagement.Application.Settings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

public sealed record RoleBody(string Name, string? Description);
public sealed record RolePermissionsBody(List<RolePermissionDto> Permissions);
public sealed record CreateUserBody(string Username, string FirstName, string LastName, string Email, string Password, Guid RoleId);
public sealed record UpdateUserBody(string FirstName, string LastName, string Email, Guid RoleId, bool IsActive);
public sealed record ResetPasswordBody(string Password);

/// <summary>Settings: the agency's roles, the page permission grid and its users. Controlled by the Administration page.</summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class SettingsController : ControllerBase
{
    private readonly IMediator _mediator;
    public SettingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("roles")]
    [ProducesResponseType(typeof(IReadOnlyList<RoleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Roles(CancellationToken ct) => Ok(await _mediator.Send(new ListRolesQuery(), ct));

    [HttpPost("roles")]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateRole([FromBody] RoleBody b, CancellationToken ct)
    {
        var role = await _mediator.Send(new CreateRoleCommand(b.Name, b.Description), ct);
        return Created($"/api/v1/roles/{role.Id}", role);
    }

    [HttpPut("roles/{id:guid}")]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] RoleBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateRoleCommand(id, b.Name, b.Description), ct));

    [HttpDelete("roles/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteRoleCommand(id), ct);
        return NoContent();
    }

    [HttpPut("roles/{id:guid}/permissions")]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetPermissions(Guid id, [FromBody] RolePermissionsBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SetRolePermissionsCommand(id, b.Permissions ?? new List<RolePermissionDto>()), ct));

    [HttpGet("users")]
    [ProducesResponseType(typeof(IReadOnlyList<SettingsUserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Users(CancellationToken ct) => Ok(await _mediator.Send(new ListSettingsUsersQuery(), ct));

    [HttpPost("users")]
    [ProducesResponseType(typeof(SettingsUserDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserBody b, CancellationToken ct)
    {
        var user = await _mediator.Send(new CreateSettingsUserCommand(b.Username, b.FirstName, b.LastName, b.Email, b.Password, b.RoleId), ct);
        return Created($"/api/v1/users/{user.Id}", user);
    }

    [HttpPut("users/{id:guid}")]
    [ProducesResponseType(typeof(SettingsUserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateSettingsUserCommand(id, b.FirstName, b.LastName, b.Email, b.RoleId, b.IsActive), ct));

    [HttpPost("users/{id:guid}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordBody b, CancellationToken ct)
    {
        await _mediator.Send(new ResetSettingsUserPasswordCommand(id, b.Password), ct);
        return NoContent();
    }
}
