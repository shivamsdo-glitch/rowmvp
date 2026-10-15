using Aurigo.RowManagement.Application.AcquisitionPlans;
using Aurigo.RowManagement.Application.AcquisitionRequests;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.PlanReviews;
using Aurigo.RowManagement.Application.RightsOfEntry;
using Aurigo.RowManagement.Application.StaffReports;
using Aurigo.RowManagement.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

public sealed record RightOfEntryBody(RightOfEntryStatus Status, DateOnly? RequestedOn, DateOnly? GrantedOn, DateOnly? ExpiresOn, string? GrantedBy, string? Purpose, string? Notes);
public sealed record PlanReviewBody(ReviewDiscipline Discipline, ReviewOutcome Outcome, DateOnly ReviewedOn, string ReviewerName, string? Notes);
public sealed record StaffReportCreateBody(ReportKind Kind, string Title, string Body);
public sealed record StaffReportEditBody(string Title, string Body);
public sealed record DecisionBody(string? Note);
public sealed record AcquisitionRequestBody(Guid? ProjectId, RequesterType RequesterType, string RequesterName, string? RequesterEmail, string Property, string? DesiredParameters, decimal? EstimatedAmount);
public sealed record PlanReviewGateBody(bool Enabled);

/// <summary>Rights of entry, plan reviews, staff reports and certificates of necessity on a parcel (ROW-077, 239, 240).</summary>
[ApiController]
[Authorize]
[Route("api/v1/parcels/{parcelId:guid}")]
public sealed class ParcelAcquisitionController : ControllerBase
{
    private readonly IMediator _mediator;
    public ParcelAcquisitionController(IMediator mediator) => _mediator = mediator;

    [HttpGet("rights-of-entry")]
    [ProducesResponseType(typeof(IReadOnlyList<RightOfEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Roes(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListParcelRightsOfEntryQuery(parcelId), ct));

    [HttpPost("rights-of-entry")]
    [ProducesResponseType(typeof(RightOfEntryDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddRoe(Guid parcelId, [FromBody] RightOfEntryBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/rights-of-entry", await _mediator.Send(new AddRightOfEntryCommand(parcelId, b.Status, b.RequestedOn, b.GrantedOn, b.ExpiresOn, b.GrantedBy, b.Purpose, b.Notes), ct));

    [HttpPut("rights-of-entry/{id:guid}")]
    [ProducesResponseType(typeof(RightOfEntryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateRoe(Guid parcelId, Guid id, [FromBody] RightOfEntryBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateRightOfEntryCommand(parcelId, id, b.Status, b.RequestedOn, b.GrantedOn, b.ExpiresOn, b.GrantedBy, b.Purpose, b.Notes), ct));

    [HttpDelete("rights-of-entry/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveRoe(Guid parcelId, Guid id, CancellationToken ct)
    {
        await _mediator.Send(new RemoveRightOfEntryCommand(parcelId, id), ct);
        return NoContent();
    }

    [HttpGet("plan-reviews")]
    [ProducesResponseType(typeof(PlanReviewStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> PlanReviews(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new GetParcelPlanReviewsQuery(parcelId), ct));

    [HttpPost("plan-reviews")]
    [ProducesResponseType(typeof(PlanReviewStatusDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddPlanReview(Guid parcelId, [FromBody] PlanReviewBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/plan-reviews", await _mediator.Send(new AddPlanReviewCommand(parcelId, b.Discipline, b.Outcome, b.ReviewedOn, b.ReviewerName, b.Notes), ct));

    [HttpDelete("plan-reviews/{id:guid}")]
    [ProducesResponseType(typeof(PlanReviewStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemovePlanReview(Guid parcelId, Guid id, CancellationToken ct) => Ok(await _mediator.Send(new RemovePlanReviewCommand(parcelId, id), ct));

    [HttpGet("staff-reports")]
    [ProducesResponseType(typeof(IReadOnlyList<StaffReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reports(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListStaffReportsQuery(parcelId), ct));

    [HttpPost("staff-reports")]
    [ProducesResponseType(typeof(StaffReportDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateReport(Guid parcelId, [FromBody] StaffReportCreateBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/staff-reports", await _mediator.Send(new CreateStaffReportCommand(parcelId, b.Kind, b.Title, b.Body), ct));

    [HttpPut("staff-reports/{id:guid}")]
    [ProducesResponseType(typeof(StaffReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateReport(Guid parcelId, Guid id, [FromBody] StaffReportEditBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateStaffReportCommand(parcelId, id, b.Title, b.Body), ct));

    [HttpPost("staff-reports/{id:guid}/submit")]
    [ProducesResponseType(typeof(StaffReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitReport(Guid parcelId, Guid id, CancellationToken ct) => Ok(await _mediator.Send(new SubmitStaffReportCommand(parcelId, id), ct));

    [HttpPost("staff-reports/{id:guid}/approve")]
    [ProducesResponseType(typeof(StaffReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ApproveReport(Guid parcelId, Guid id, [FromBody] DecisionBody? b, CancellationToken ct) =>
        Ok(await _mediator.Send(new DecideStaffReportCommand(parcelId, id, true, b?.Note), ct));

    [HttpPost("staff-reports/{id:guid}/reject")]
    [ProducesResponseType(typeof(StaffReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RejectReport(Guid parcelId, Guid id, [FromBody] DecisionBody? b, CancellationToken ct) =>
        Ok(await _mediator.Send(new DecideStaffReportCommand(parcelId, id, false, b?.Note), ct));

    [HttpDelete("staff-reports/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteReport(Guid parcelId, Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteStaffReportCommand(parcelId, id), ct);
        return NoContent();
    }
}

/// <summary>The report of every right of entry, the acquisition-request inbox, the project plan and the plan-review gate setting (ROW-077, 080, 239, 241).</summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class AcquisitionController : ControllerBase
{
    private readonly IMediator _mediator;
    public AcquisitionController(IMediator mediator) => _mediator = mediator;

    [HttpGet("rights-of-entry")]
    [ProducesResponseType(typeof(PagedResult<RightOfEntryReportRowDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RoeReport([FromQuery] Guid? projectId, [FromQuery] string? status, [FromQuery] int? expiringWithinDays, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await _mediator.Send(new RightsOfEntryReportQuery(projectId, status, expiringWithinDays, page, pageSize), ct));

    [HttpGet("acquisition-requests")]
    [ProducesResponseType(typeof(PagedResult<AcquisitionRequestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Requests([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(await _mediator.Send(new ListAcquisitionRequestsQuery(status, page, pageSize), ct));

    [HttpGet("acquisition-requests/{id:guid}")]
    [ProducesResponseType(typeof(AcquisitionRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRequest(Guid id, CancellationToken ct) => Ok(await _mediator.Send(new GetAcquisitionRequestQuery(id), ct));

    [HttpPost("acquisition-requests")]
    [ProducesResponseType(typeof(AcquisitionRequestDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Submit([FromBody] AcquisitionRequestBody b, CancellationToken ct) =>
        Created("/api/v1/acquisition-requests", await _mediator.Send(new SubmitAcquisitionRequestCommand(b.ProjectId, b.RequesterType, b.RequesterName, b.RequesterEmail, b.Property, b.DesiredParameters, b.EstimatedAmount), ct));

    [HttpPost("acquisition-requests/{id:guid}/approve")]
    [ProducesResponseType(typeof(AcquisitionRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] DecisionBody? b, CancellationToken ct) => Ok(await _mediator.Send(new DecideAcquisitionRequestCommand(id, true, b?.Note), ct));

    [HttpPost("acquisition-requests/{id:guid}/reject")]
    [ProducesResponseType(typeof(AcquisitionRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] DecisionBody? b, CancellationToken ct) => Ok(await _mediator.Send(new DecideAcquisitionRequestCommand(id, false, b?.Note), ct));

    [HttpPost("acquisition-requests/{id:guid}/withdraw")]
    [ProducesResponseType(typeof(AcquisitionRequestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct) => Ok(await _mediator.Send(new WithdrawAcquisitionRequestCommand(id), ct));

    [HttpGet("projects/{projectId:guid}/acquisition-plan")]
    [ProducesResponseType(typeof(AcquisitionPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Plan(Guid projectId, CancellationToken ct) => Ok(await _mediator.Send(new GetAcquisitionPlanQuery(projectId), ct));

    [HttpGet("agency/plan-review-gate")]
    [ProducesResponseType(typeof(PlanReviewGateSettingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGate(CancellationToken ct) => Ok(await _mediator.Send(new GetPlanReviewGateQuery(), ct));

    [HttpPut("agency/plan-review-gate")]
    [ProducesResponseType(typeof(PlanReviewGateSettingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetGate([FromBody] PlanReviewGateBody b, CancellationToken ct) => Ok(await _mediator.Send(new SetPlanReviewGateCommand(b.Enabled), ct));
}
