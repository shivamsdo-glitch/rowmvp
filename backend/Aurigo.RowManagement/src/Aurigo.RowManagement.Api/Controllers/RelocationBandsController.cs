using Aurigo.RowManagement.Application.ParcelTabs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

public sealed record RelocationBandsBody(List<RelocationBandInput>? Bands);

/// <summary>The approval chain relocation payments go through: dollar bands and the roles that sign each (ADR 0026). Read by anyone who reads the Relocation tab; changed on the Configuration page.</summary>
[ApiController]
[Authorize]
[Route("api/v1/relocation-payment-bands")]
public sealed class RelocationBandsController : ControllerBase
{
    private readonly IMediator _mediator;
    public RelocationBandsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(RelocationBandsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await _mediator.Send(new GetRelocationBandsQuery(), ct));

    [HttpPut]
    [ProducesResponseType(typeof(RelocationBandsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Save([FromBody] RelocationBandsBody b, CancellationToken ct) => Ok(await _mediator.Send(new SaveRelocationBandsCommand(b.Bands ?? new List<RelocationBandInput>()), ct));
}
