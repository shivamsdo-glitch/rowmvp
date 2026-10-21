using Aurigo.RowManagement.Application.Dashboard;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

/// <summary>The signed-in person's dashboard (what they did, what is waiting) and the manager roll-up (ADR 0022). Governed by the <c>dashboard</c> page.</summary>
[ApiController]
[Authorize]
[Route("api/v1/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;
    public DashboardController(IMediator mediator) => _mediator = mediator;

    [HttpGet("me")]
    [ProducesResponseType(typeof(MyDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct) => Ok(await _mediator.Send(new GetMyDashboardQuery(), ct));

    /// <summary>Everyone's work added up. 403 unless the caller is a project manager or an administrator.</summary>
    [HttpGet("team")]
    [ProducesResponseType(typeof(TeamDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Team(CancellationToken ct) => Ok(await _mediator.Send(new GetTeamDashboardQuery(), ct));
}
