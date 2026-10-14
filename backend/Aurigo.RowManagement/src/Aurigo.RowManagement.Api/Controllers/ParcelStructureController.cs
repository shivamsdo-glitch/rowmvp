using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

public sealed record GroupBody(string Name, string? Description);
public sealed record TractBody(string Number, string? Description, List<string>? TaxParcelNumbers);

/// <summary>A project's parcel structure: groups (the second WBS level), parent tracts and the parcel roll-up (ROW-002, 006, 233).</summary>
[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}")]
public sealed class ParcelStructureController : ControllerBase
{
    private readonly IMediator _mediator;
    public ParcelStructureController(IMediator mediator) => _mediator = mediator;

    [HttpGet("groups")]
    [ProducesResponseType(typeof(IReadOnlyList<ParcelGroupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListGroups(Guid projectId, CancellationToken ct) => Ok(await _mediator.Send(new ListParcelGroupsQuery(projectId), ct));

    [HttpPost("groups")]
    [ProducesResponseType(typeof(ParcelGroupDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateGroup(Guid projectId, [FromBody] GroupBody b, CancellationToken ct) =>
        Created($"/api/v1/projects/{projectId}/groups", await _mediator.Send(new SaveParcelGroupCommand(projectId, null, b.Name, b.Description), ct));

    [HttpPut("groups/{groupId:guid}")]
    [ProducesResponseType(typeof(ParcelGroupDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateGroup(Guid projectId, Guid groupId, [FromBody] GroupBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SaveParcelGroupCommand(projectId, groupId, b.Name, b.Description), ct));

    [HttpDelete("groups/{groupId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteGroup(Guid projectId, Guid groupId, CancellationToken ct)
    {
        await _mediator.Send(new DeleteParcelGroupCommand(projectId, groupId), ct);
        return NoContent();
    }

    [HttpGet("tracts")]
    [ProducesResponseType(typeof(IReadOnlyList<ParentTractDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTracts(Guid projectId, CancellationToken ct) => Ok(await _mediator.Send(new ListParentTractsQuery(projectId), ct));

    [HttpPost("tracts")]
    [ProducesResponseType(typeof(ParentTractDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTract(Guid projectId, [FromBody] TractBody b, CancellationToken ct) =>
        Created($"/api/v1/projects/{projectId}/tracts", await _mediator.Send(new SaveParentTractCommand(projectId, null, b.Number, b.Description, b.TaxParcelNumbers), ct));

    [HttpPut("tracts/{tractId:guid}")]
    [ProducesResponseType(typeof(ParentTractDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTract(Guid projectId, Guid tractId, [FromBody] TractBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SaveParentTractCommand(projectId, tractId, b.Number, b.Description, b.TaxParcelNumbers), ct));

    /// <summary>Counts and areas of the project's live parcels by status and by group, optionally for one group only.</summary>
    [HttpGet("parcel-rollup")]
    [ProducesResponseType(typeof(ParcelRollupDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Rollup(Guid projectId, [FromQuery] Guid? groupId, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetParcelRollupQuery(projectId, groupId), ct));
}

public sealed record StatusBody(string Name, string Color, int SortOrder, bool IsActive, bool IsInitial);
public sealed record CustomFieldBody(string Key, string Label, CustomFieldType FieldType, List<string>? Options, bool IsRequired, bool IsActive, int SortOrder);
public sealed record DeadlineRuleBody(string Name, string TriggerMilestone, string TargetMilestone, int Days, bool IsActive);
public sealed record StepGateBody(List<string>? ExtraOverrideRoles);

/// <summary>The agency's parcel rules: statuses, custom fields, deadline rules, workflows and who may override step order (ROW-005, 011, 071-073).</summary>
[ApiController]
[Authorize]
[Route("api/v1/parcel-rules")]
public sealed class ParcelRulesController : ControllerBase
{
    private readonly IMediator _mediator;
    public ParcelRulesController(IMediator mediator) => _mediator = mediator;

    [HttpGet("statuses")]
    [ProducesResponseType(typeof(IReadOnlyList<ParcelStatusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Statuses(CancellationToken ct) => Ok(await _mediator.Send(new ListParcelStatusesQuery(), ct));

    [HttpPost("statuses")]
    [ProducesResponseType(typeof(ParcelStatusDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateStatus([FromBody] StatusBody b, CancellationToken ct) =>
        Created("/api/v1/parcel-rules/statuses", await _mediator.Send(new SaveParcelStatusCommand(null, b.Name, b.Color, b.SortOrder, b.IsActive, b.IsInitial), ct));

    [HttpPut("statuses/{id:guid}")]
    [ProducesResponseType(typeof(ParcelStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] StatusBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SaveParcelStatusCommand(id, b.Name, b.Color, b.SortOrder, b.IsActive, b.IsInitial), ct));

    [HttpGet("custom-fields")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomFieldDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CustomFields(CancellationToken ct) => Ok(await _mediator.Send(new ListCustomFieldsQuery(), ct));

    [HttpPost("custom-fields")]
    [ProducesResponseType(typeof(CustomFieldDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCustomField([FromBody] CustomFieldBody b, CancellationToken ct) =>
        Created("/api/v1/parcel-rules/custom-fields", await _mediator.Send(new SaveCustomFieldCommand(null, b.Key, b.Label, b.FieldType, b.Options, b.IsRequired, b.IsActive, b.SortOrder), ct));

    [HttpPut("custom-fields/{id:guid}")]
    [ProducesResponseType(typeof(CustomFieldDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCustomField(Guid id, [FromBody] CustomFieldBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SaveCustomFieldCommand(id, b.Key, b.Label, b.FieldType, b.Options, b.IsRequired, b.IsActive, b.SortOrder), ct));

    [HttpGet("deadline-rules")]
    [ProducesResponseType(typeof(IReadOnlyList<DeadlineRuleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeadlineRules(CancellationToken ct) => Ok(await _mediator.Send(new ListDeadlineRulesQuery(), ct));

    [HttpPost("deadline-rules")]
    [ProducesResponseType(typeof(DeadlineRuleDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateDeadlineRule([FromBody] DeadlineRuleBody b, CancellationToken ct) =>
        Created("/api/v1/parcel-rules/deadline-rules", await _mediator.Send(new SaveDeadlineRuleCommand(null, b.Name, b.TriggerMilestone, b.TargetMilestone, b.Days, b.IsActive), ct));

    [HttpPut("deadline-rules/{id:guid}")]
    [ProducesResponseType(typeof(DeadlineRuleDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateDeadlineRule(Guid id, [FromBody] DeadlineRuleBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SaveDeadlineRuleCommand(id, b.Name, b.TriggerMilestone, b.TargetMilestone, b.Days, b.IsActive), ct));

    [HttpGet("workflows")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Workflows(CancellationToken ct) => Ok(await _mediator.Send(new ListWorkflowsQuery(), ct));

    [HttpGet("step-gate")]
    [ProducesResponseType(typeof(StepGateSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> StepGate(CancellationToken ct) => Ok(await _mediator.Send(new GetStepGateSettingsQuery(), ct));

    [HttpPut("step-gate")]
    [ProducesResponseType(typeof(StepGateSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetStepGate([FromBody] StepGateBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SetStepGateSettingsCommand(b.ExtraOverrideRoles ?? new List<string>()), ct));
}

/// <summary>Computed next-action reminders (ROW-074): deadlines that are overdue or due soon. Nothing is emailed.</summary>
[ApiController]
[Authorize]
[Route("api/v1/reminders")]
public sealed class RemindersController : ControllerBase
{
    private readonly IMediator _mediator;
    public RemindersController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReminderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int withinDays = 7, CancellationToken ct = default) =>
        Ok(await _mediator.Send(new ListRemindersQuery(withinDays), ct));
}
