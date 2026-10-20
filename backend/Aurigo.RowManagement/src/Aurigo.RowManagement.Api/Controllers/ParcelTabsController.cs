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

public sealed record OwnershipBody(
    string? OwnerName, string? OwnerType, List<OwnershipAddressInput>? Addresses, List<OwnershipContactInput>? Contacts);

public sealed record EncumbranceBody(
    string? EncumbranceType, string? DocumentTitle, DateOnly? DocumentDate, DateOnly? DateRecorded, string? DocumentNumber, bool IsUnrecorded,
    string? Grantor, string? Grantee, DateOnly? ExceptionSubmittedDate, DateOnly? ExceptionApprovedDate, string? ImpactAnalysis, string? RecommendedCure,
    string? CureStatus, string? ReviewNotes, List<ContactLogLinkInput>? Links);

/// <summary>The Contact Log, Easements, Ownership Details and Title Encumbrances tabs of a parcel (Phase 1.5, ADR 0017). Each is governed by its own page's permissions.</summary>
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

    [HttpGet("ownerships")]
    [ProducesResponseType(typeof(IReadOnlyList<OwnershipDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ownerships(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListOwnershipsQuery(parcelId), ct));

    [HttpPost("ownerships")]
    [ProducesResponseType(typeof(OwnershipDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddOwnership(Guid parcelId, [FromBody] OwnershipBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/ownerships", await _mediator.Send(new AddOwnershipCommand(parcelId, b.OwnerName ?? "", b.OwnerType, b.Addresses, b.Contacts), ct));

    [HttpPut("ownerships/{ownershipId:guid}")]
    [ProducesResponseType(typeof(OwnershipDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateOwnership(Guid parcelId, Guid ownershipId, [FromBody] OwnershipBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateOwnershipCommand(parcelId, ownershipId, b.OwnerName ?? "", b.OwnerType, b.Addresses, b.Contacts), ct));

    [HttpDelete("ownerships/{ownershipId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveOwnership(Guid parcelId, Guid ownershipId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveOwnershipCommand(parcelId, ownershipId), ct);
        return NoContent();
    }

    [HttpGet("encumbrances")]
    [ProducesResponseType(typeof(IReadOnlyList<EncumbranceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Encumbrances(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListEncumbrancesQuery(parcelId), ct));

    [HttpPost("encumbrances")]
    [ProducesResponseType(typeof(EncumbranceDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddEncumbrance(Guid parcelId, [FromBody] EncumbranceBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/encumbrances", await _mediator.Send(new AddEncumbranceCommand(
            parcelId, b.EncumbranceType ?? "", b.DocumentTitle ?? "", b.DocumentDate, b.DateRecorded, b.DocumentNumber, b.IsUnrecorded, b.Grantor ?? "", b.Grantee ?? "",
            b.ExceptionSubmittedDate, b.ExceptionApprovedDate, b.ImpactAnalysis, b.RecommendedCure, b.CureStatus, b.ReviewNotes, b.Links), ct));

    [HttpPut("encumbrances/{encumbranceId:guid}")]
    [ProducesResponseType(typeof(EncumbranceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEncumbrance(Guid parcelId, Guid encumbranceId, [FromBody] EncumbranceBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateEncumbranceCommand(
            parcelId, encumbranceId, b.EncumbranceType ?? "", b.DocumentTitle ?? "", b.DocumentDate, b.DateRecorded, b.DocumentNumber, b.IsUnrecorded, b.Grantor ?? "", b.Grantee ?? "",
            b.ExceptionSubmittedDate, b.ExceptionApprovedDate, b.ImpactAnalysis, b.RecommendedCure, b.CureStatus, b.ReviewNotes, b.Links), ct));

    [HttpDelete("encumbrances/{encumbranceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveEncumbrance(Guid parcelId, Guid encumbranceId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveEncumbranceCommand(parcelId, encumbranceId), ct);
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
