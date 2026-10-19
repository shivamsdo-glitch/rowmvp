using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

public sealed record ParcelBody(
    Guid? ProjectId, string? Number, Guid? ParentTractId, Guid? GroupId, string? LegalDescription, string? Address, decimal AreaSqFt,
    string? PropertyType, Guid? StatusId, AcquisitionType AcquisitionType, Dictionary<string, string?>? CustomValues,
    string? CadNumber = null, string? AcquisitionInterestType = null, DateOnly? PossessionDate = null, DateOnly? ProposedPossessionDate = null,
    string? Segment = null, string? Priority = null, string? PossessionType = null, string? ParcelLocation = null, bool IsActiveParcel = true);
public sealed record BatchUpdateBody(List<Guid> ParcelIds, Guid? StatusId, Guid? GroupId, bool ClearGroup);
public sealed record SplitBody(List<SplitPart> Parts);
public sealed record CombineBody(List<Guid> ParcelIds, string? Number);
public sealed record InterestBody(InterestType Type, decimal AreaSqFt, string? Notes);
public sealed record MilestoneBody(string? Name, DateOnly? DueOn, DateOnly? CompletedOn, string? Notes);
public sealed record SiteVisitBody(DateOnly VisitedOn, string? Notes, bool OwnerContactConfirmed);
public sealed record NoticeBody(DateOnly? IssuedOn, NoticeMethod? Method);
public sealed record AdvanceBody(string TargetStepKey, string? OverrideReason);

/// <summary>Parcels: records, split/combine, batch updates, milestones, notice, site visits and ordered workflow steps (ROW-005..010, 071..074, 233..237).</summary>
[ApiController]
[Authorize]
[Route("api/v1/parcels")]
public sealed class ParcelsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ParcelsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ParcelSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? projectId, [FromQuery] Guid? groupId, [FromQuery] Guid? statusId, [FromQuery] Guid? tractId, [FromQuery] string? search,
        [FromQuery] bool includeSuperseded = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await _mediator.Send(new ListParcelsQuery(projectId, groupId, statusId, tractId, search, includeSuperseded, page, pageSize), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await _mediator.Send(new GetParcelQuery(id), ct));

    /// <summary>Creates a parcel. Leave <c>number</c> empty for the next number in the project and <c>statusId</c> empty for the initial status.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] ParcelBody b, CancellationToken ct)
    {
        var created = await _mediator.Send(new CreateParcelCommand(
            b.ProjectId ?? Guid.Empty, b.Number, b.ParentTractId, b.GroupId, b.LegalDescription, b.Address, b.AreaSqFt, b.PropertyType,
            b.StatusId, b.AcquisitionType, b.CustomValues, b.CadNumber, b.AcquisitionInterestType, b.PossessionDate, b.ProposedPossessionDate,
            b.Segment, b.Priority, b.PossessionType, b.ParcelLocation, b.IsActiveParcel), ct);
        return Created($"/api/v1/parcels/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] ParcelBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateParcelCommand(
            id, b.Number, b.ParentTractId, b.GroupId, b.LegalDescription, b.Address, b.AreaSqFt, b.PropertyType, b.StatusId, b.AcquisitionType, b.CustomValues,
            b.CadNumber, b.AcquisitionInterestType, b.PossessionDate, b.ProposedPossessionDate, b.Segment, b.Priority, b.PossessionType, b.ParcelLocation,
            b.IsActiveParcel), ct));

    /// <summary>Sets the status and/or group of many parcels at once; all change or none do.</summary>
    [HttpPost("batch-update")]
    [ProducesResponseType(typeof(BatchUpdateResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> BatchUpdate([FromBody] BatchUpdateBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new BatchUpdateParcelsCommand(b.ParcelIds ?? new List<Guid>(), b.StatusId, b.GroupId, b.ClearGroup), ct));

    [HttpPost("{id:guid}/split")]
    [ProducesResponseType(typeof(ParcelSplitResultDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Split(Guid id, [FromBody] SplitBody b, CancellationToken ct)
    {
        var result = await _mediator.Send(new SplitParcelCommand(id, b.Parts ?? new List<SplitPart>()), ct);
        return Created($"/api/v1/parcels/{id}", result);
    }

    [HttpPost("combine")]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Combine([FromBody] CombineBody b, CancellationToken ct)
    {
        var created = await _mediator.Send(new CombineParcelsCommand(b.ParcelIds ?? new List<Guid>(), b.Number), ct);
        return Created($"/api/v1/parcels/{created.Id}", created);
    }

    // ── acquisition interests ──

    [HttpPost("{id:guid}/interests")]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddInterest(Guid id, [FromBody] InterestBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{id}", await _mediator.Send(new SaveParcelInterestCommand(id, null, b.Type, b.AreaSqFt, b.Notes), ct));

    [HttpPut("{id:guid}/interests/{interestId:guid}")]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateInterest(Guid id, Guid interestId, [FromBody] InterestBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SaveParcelInterestCommand(id, interestId, b.Type, b.AreaSqFt, b.Notes), ct));

    [HttpDelete("{id:guid}/interests/{interestId:guid}")]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteInterest(Guid id, Guid interestId, CancellationToken ct) =>
        Ok(await _mediator.Send(new DeleteParcelInterestCommand(id, interestId), ct));

    // ── milestones ──

    [HttpPost("{id:guid}/milestones")]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddMilestone(Guid id, [FromBody] MilestoneBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{id}", await _mediator.Send(new AddParcelMilestoneCommand(id, b.Name ?? string.Empty, b.DueOn, b.CompletedOn, b.Notes), ct));

    [HttpPut("{id:guid}/milestones/{milestoneId:guid}")]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMilestone(Guid id, Guid milestoneId, [FromBody] MilestoneBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateParcelMilestoneCommand(id, milestoneId, b.DueOn, b.CompletedOn, b.Notes), ct));

    [HttpDelete("{id:guid}/milestones/{milestoneId:guid}")]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteMilestone(Guid id, Guid milestoneId, CancellationToken ct) =>
        Ok(await _mediator.Send(new DeleteParcelMilestoneCommand(id, milestoneId), ct));

    // ── site visits ──

    [HttpPost("{id:guid}/site-visits")]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddSiteVisit(Guid id, [FromBody] SiteVisitBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{id}", await _mediator.Send(new AddParcelSiteVisitCommand(id, b.VisitedOn, b.Notes, b.OwnerContactConfirmed), ct));

    [HttpDelete("{id:guid}/site-visits/{visitId:guid}")]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteSiteVisit(Guid id, Guid visitId, CancellationToken ct) =>
        Ok(await _mediator.Send(new DeleteParcelSiteVisitCommand(id, visitId), ct));

    // ── notice to owner and workflow ──

    [HttpPut("{id:guid}/notice")]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordNotice(Guid id, [FromBody] NoticeBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new RecordNoticeToOwnerCommand(id, b.IssuedOn, b.Method), ct));

    /// <summary>Moves the parcel to a workflow step. Out-of-order moves need an override role and a reason; offer steps need the project gate open.</summary>
    [HttpPost("{id:guid}/advance")]
    [ProducesResponseType(typeof(ParcelDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Advance(Guid id, [FromBody] AdvanceBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new AdvanceParcelStepCommand(id, b.TargetStepKey ?? string.Empty, b.OverrideReason), ct));
}
