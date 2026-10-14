using Aurigo.RowManagement.Application.Agency;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

public sealed record AgencyTimeZoneBody(string TimeZoneId);

/// <summary>Agency-wide settings. Today: the time zone days are counted in (projects can override it).</summary>
[ApiController]
[Authorize]
[Route("api/v1/agency")]
public sealed class AgencyController : ControllerBase
{
    private readonly IMediator _mediator;
    public AgencyController(IMediator mediator) => _mediator = mediator;

    [HttpGet("time-zone")]
    [ProducesResponseType(typeof(AgencyTimeZoneDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTimeZone(CancellationToken ct) => Ok(await _mediator.Send(new GetAgencyTimeZoneQuery(), ct));

    [HttpPut("time-zone")]
    [ProducesResponseType(typeof(AgencyTimeZoneDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetTimeZone([FromBody] AgencyTimeZoneBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SetAgencyTimeZoneCommand(b.TimeZoneId), ct));
}
