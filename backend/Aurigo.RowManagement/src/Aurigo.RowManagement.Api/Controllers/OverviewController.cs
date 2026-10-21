using Aurigo.RowManagement.Application.ParcelTabs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

/// <summary>
/// Searches across every parcel for the Title &amp; Survey, Appraisal and Negotiation &amp; Closing screens (ADR 0021): find a record by parcel number, address, owner
/// name or its own ID, then work on it through the parcel tab's own endpoints. Each search is governed by the page of the parcel tab it searches.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/overview")]
public sealed class OverviewController : ControllerBase
{
    private readonly IMediator _mediator;
    public OverviewController(IMediator mediator) => _mediator = mediator;

    [HttpGet("appraisals")]
    [ProducesResponseType(typeof(IReadOnlyList<AppraisalOverviewRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Appraisals([FromQuery] string? search, CancellationToken ct) => Ok(await _mediator.Send(new AppraisalOverviewQuery(search), ct));

    [HttpGet("negotiations")]
    [ProducesResponseType(typeof(IReadOnlyList<NegotiationOverviewRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Negotiations([FromQuery] string? search, CancellationToken ct) => Ok(await _mediator.Send(new NegotiationOverviewQuery(search), ct));

    [HttpGet("title-closings")]
    [ProducesResponseType(typeof(IReadOnlyList<TitleClosingOverviewRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> TitleClosings([FromQuery] string? search, CancellationToken ct) => Ok(await _mediator.Send(new TitleClosingOverviewQuery(search), ct));

    [HttpGet("encumbrances")]
    [ProducesResponseType(typeof(IReadOnlyList<EncumbranceOverviewRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Encumbrances([FromQuery] string? search, CancellationToken ct) => Ok(await _mediator.Send(new EncumbranceOverviewQuery(search), ct));

    [HttpGet("easements")]
    [ProducesResponseType(typeof(IReadOnlyList<EasementOverviewRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Easements([FromQuery] string? search, CancellationToken ct) => Ok(await _mediator.Send(new EasementOverviewQuery(search), ct));
}
