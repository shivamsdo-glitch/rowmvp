using Aurigo.RowManagement.Application.ParcelTabs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

public sealed record ContactLogBody(
    DateTime ContactedAt, string? Mode, string? ContactedBy, string? ContactPerson, string? Notes, List<ContactLogLinkInput>? Links);
public sealed record EasementBody(
    string? Number, string? EasementType, string? Description, DateOnly? StartDate, DateOnly? EndDate, string? StartStation, string? EndStation);

/// <summary>The Contact Log and Easements tabs of a parcel (Phase 1.5, ADR 0017). Each is governed by its own page's permissions.</summary>
[ApiController]
[Authorize]
[Route("api/v1/parcels/{parcelId:guid}")]
public sealed class ParcelTabsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ParcelTabsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("contact-log")]
    [ProducesResponseType(typeof(IReadOnlyList<ContactLogEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ContactLog(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListContactLogQuery(parcelId), ct));

    [HttpPost("contact-log")]
    [ProducesResponseType(typeof(ContactLogEntryDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddContactLog(Guid parcelId, [FromBody] ContactLogBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/contact-log", await _mediator.Send(new AddContactLogEntryCommand(parcelId, b.ContactedAt, b.Mode, b.ContactedBy, b.ContactPerson, b.Notes, b.Links), ct));

    [HttpPut("contact-log/{entryId:guid}")]
    [ProducesResponseType(typeof(ContactLogEntryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateContactLog(Guid parcelId, Guid entryId, [FromBody] ContactLogBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateContactLogEntryCommand(parcelId, entryId, b.ContactedAt, b.Mode, b.ContactedBy, b.ContactPerson, b.Notes, b.Links), ct));

    [HttpDelete("contact-log/{entryId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveContactLog(Guid parcelId, Guid entryId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveContactLogEntryCommand(parcelId, entryId), ct);
        return NoContent();
    }

    [HttpGet("easements")]
    [ProducesResponseType(typeof(IReadOnlyList<EasementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Easements(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListEasementsQuery(parcelId), ct));

    [HttpPost("easements")]
    [ProducesResponseType(typeof(EasementDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddEasement(Guid parcelId, [FromBody] EasementBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/easements", await _mediator.Send(new AddEasementCommand(parcelId, b.Number ?? "", b.EasementType, b.Description, b.StartDate, b.EndDate, b.StartStation, b.EndStation), ct));

    [HttpPut("easements/{easementId:guid}")]
    [ProducesResponseType(typeof(EasementDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEasement(Guid parcelId, Guid easementId, [FromBody] EasementBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateEasementCommand(parcelId, easementId, b.Number ?? "", b.EasementType, b.Description, b.StartDate, b.EndDate, b.StartStation, b.EndStation), ct));

    [HttpDelete("easements/{easementId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveEasement(Guid parcelId, Guid easementId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveEasementCommand(parcelId, easementId), ct);
        return NoContent();
    }
}
