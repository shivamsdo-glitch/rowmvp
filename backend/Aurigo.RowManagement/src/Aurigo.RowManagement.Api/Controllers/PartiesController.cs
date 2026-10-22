using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Parties;
using Aurigo.RowManagement.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

public sealed record PartyBody(
    PartyKind Kind, string Name, string? LegalName, string? Email, string? Phone, string? AddressLine1, string? AddressLine2,
    string? City, string? State, string? PostalCode, string? Notes,
    List<PartyAddressInput>? Addresses = null, List<PartyContactInput>? Contacts = null);

/// <summary>The tax number is accepted here and never returned: responses carry the last four digits only.</summary>
public sealed record PartyTaxBody(TaxIdType? Type, string? TaxId, DateOnly? W9ReceivedOn, bool Reportable1099)
{
    public override string ToString() => $"PartyTaxBody {{ Type = {Type}, TaxId = [redacted] }}";
}

public sealed record PartyLinkUpdateBody(decimal? SharePercent, string? Notes, bool LeaseOnFile, string? LeaseDetails, string? InsuranceDetails, string? OwnerType = null, bool? DisplacementRequired = null);

/// <summary>Owners, interests, contacts, tenants and abutters (ROW-014..018).</summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class PartiesController : ControllerBase
{
    private readonly IMediator _mediator;
    public PartiesController(IMediator mediator) => _mediator = mediator;

    [HttpGet("parties")]
    [ProducesResponseType(typeof(PagedResult<PartySummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] PartyKind? kind, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await _mediator.Send(new ListPartiesQuery(search, kind, page, pageSize), ct));

    [HttpGet("parties/{id:guid}")]
    [ProducesResponseType(typeof(PartyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await _mediator.Send(new GetPartyQuery(id), ct));

    [HttpGet("parties/{id:guid}/usage")]
    [ProducesResponseType(typeof(IReadOnlyList<PartyUsageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Usage(Guid id, CancellationToken ct) => Ok(await _mediator.Send(new GetPartyUsageQuery(id), ct));

    [HttpPost("parties")]
    [ProducesResponseType(typeof(PartyDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] PartyBody b, CancellationToken ct)
    {
        var party = await _mediator.Send(new CreatePartyCommand(b.Kind, b.Name, b.LegalName, b.Email, b.Phone, b.AddressLine1, b.AddressLine2, b.City, b.State, b.PostalCode, b.Notes, b.Addresses, b.Contacts), ct);
        return Created($"/api/v1/parties/{party.Id}", party);
    }

    [HttpPut("parties/{id:guid}")]
    [ProducesResponseType(typeof(PartyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] PartyBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdatePartyCommand(id, b.Kind, b.Name, b.LegalName, b.Email, b.Phone, b.AddressLine1, b.AddressLine2, b.City, b.State, b.PostalCode, b.Notes, b.Addresses, b.Contacts), ct));

    [HttpDelete("parties/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeletePartyCommand(id), ct);
        return NoContent();
    }

    [HttpPut("parties/{id:guid}/tax-info")]
    [ProducesResponseType(typeof(PartyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetTaxInfo(Guid id, [FromBody] PartyTaxBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SetPartyTaxInfoCommand(id, b.Type, b.TaxId, b.W9ReceivedOn, b.Reportable1099), ct));

    [HttpDelete("parties/{id:guid}/tax-info/number")]
    [ProducesResponseType(typeof(PartyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearTaxId(Guid id, CancellationToken ct) => Ok(await _mediator.Send(new ClearPartyTaxIdCommand(id), ct));

    // ── around a parcel or a tract ──

    [HttpGet("parcels/{parcelId:guid}/parties")]
    [ProducesResponseType(typeof(ParcelPartiesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForParcel(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListParcelPartiesQuery(parcelId), ct));

    [HttpPost("parcels/{parcelId:guid}/parties")]
    [ProducesResponseType(typeof(PartyLinkDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddToParcel(Guid parcelId, [FromBody] PartyLinkInput body, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/parties", await _mediator.Send(new AddParcelPartyCommand(parcelId, body), ct));

    [HttpGet("projects/{projectId:guid}/tracts/{tractId:guid}/parties")]
    [ProducesResponseType(typeof(ParcelPartiesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForTract(Guid projectId, Guid tractId, CancellationToken ct) => Ok(await _mediator.Send(new ListTractPartiesQuery(projectId, tractId), ct));

    [HttpPost("projects/{projectId:guid}/tracts/{tractId:guid}/parties")]
    [ProducesResponseType(typeof(PartyLinkDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddToTract(Guid projectId, Guid tractId, [FromBody] PartyLinkInput body, CancellationToken ct) =>
        Created($"/api/v1/projects/{projectId}/tracts/{tractId}/parties", await _mediator.Send(new AddTractPartyCommand(projectId, tractId, body), ct));

    [HttpPut("party-links/{linkId:guid}")]
    [ProducesResponseType(typeof(PartyLinkDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLink(Guid linkId, [FromBody] PartyLinkUpdateBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdatePartyLinkCommand(linkId, b.SharePercent, b.Notes, b.LeaseOnFile, b.LeaseDetails, b.InsuranceDetails, b.OwnerType, b.DisplacementRequired), ct));

    [HttpDelete("party-links/{linkId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteLink(Guid linkId, CancellationToken ct)
    {
        await _mediator.Send(new DeletePartyLinkCommand(linkId), ct);
        return NoContent();
    }
}
