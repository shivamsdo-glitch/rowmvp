using Aurigo.RowManagement.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

/// <summary>Smoke endpoint proving the role gate end to end: 401 without a token, 403 without the Admin role.</summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class AdminController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { message = "pong", roles = User.FindAll("role").Select(c => c.Value) });
}
