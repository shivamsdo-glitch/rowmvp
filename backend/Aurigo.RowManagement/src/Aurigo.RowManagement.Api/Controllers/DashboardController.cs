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

    /// <summary>The ROW Director's view: every project with its health and cost, and where the time goes by step and team. 403 unless project manager or administrator.</summary>
    [HttpGet("program")]
    [ProducesResponseType(typeof(ProgramDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Program(CancellationToken ct) => Ok(await _mediator.Send(new GetProgramDashboardQuery(), ct));

    /// <summary>One project in depth, with every parcel and what is going on with it. 403 unless project manager or administrator.</summary>
    [HttpGet("projects/{projectId:guid}")]
    [ProducesResponseType(typeof(ProjectDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Project(Guid projectId, CancellationToken ct) => Ok(await _mediator.Send(new GetProjectDashboardQuery(projectId), ct));
}
