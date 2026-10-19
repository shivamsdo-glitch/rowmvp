using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

public sealed record ProjectBody(
    string? Number, string Name, string? Description, string? Region, string? District, OversightType OversightType,
    string? FundingSource, decimal? FundingAmount, decimal? DesignEstimate,
    DateOnly? LettingDate, DateOnly? PlanTurnInDate, DateOnly? ConstructionStartDate, DateOnly? ConstructionEndDate,
    string? Phase = null, string? Location = null, Guid? ProjectManagerUserId = null, string? DocumentFolderStructure = null,
    string? DocumentProperties = null, string? ConstructionProjectManager = null, DateOnly? RightOfWayClearDate = null,
    string? LinkedSource = null, string? LinkedProjectNumber = null);
public sealed record NepaBody(NepaDecisionType? DecisionType, DateOnly? DecisionDate);
public sealed record ExceptionBody(AcquisitionExceptionType? ExceptionType, DateOnly? ApprovedOn, string? Reason);
public sealed record CostEstimateBody(decimal LandCost, decimal ImprovementsCost, decimal RelocationCost, decimal IndirectCost, string? Notes);
public sealed record BudgetBody(decimal Amount, string? Notes);
public sealed record AuthorizationBody(
    AuthorizationType Type, decimal AuthorizedAmount, decimal? FederalSharePercent,
    DateOnly AuthorizedOn, string? ReferenceNumber, bool ProgrammingConfirmed, string? Notes);
public sealed record NumberSettingsBody(string Pattern, int FiscalYearStartMonth);
public sealed record TimeZoneBody(string? TimeZoneId);

/// <summary>ROW projects: record, versioned estimates, annual budgets, authorizations and the acquisition gate (ROW-001..004, 012, 013, 076, 238).</summary>
[ApiController]
[Authorize]
[Route("api/v1/projects")]
public sealed class ProjectsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ProjectsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProjectSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search, [FromQuery] string? region, [FromQuery] string? district,
        [FromQuery] OversightType? oversightType, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await _mediator.Send(new ListProjectsQuery(search, region, district, oversightType, page, pageSize), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await _mediator.Send(new GetProjectQuery(id), ct));

    /// <summary>Creates a project. Leave <c>number</c> empty to generate one from the tenant format.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] ProjectBody b, CancellationToken ct)
    {
        var created = await _mediator.Send(new CreateProjectCommand(
            b.Number, b.Name, b.Description, b.Region, b.District, b.OversightType, b.FundingSource, b.FundingAmount,
            b.DesignEstimate, b.LettingDate, b.PlanTurnInDate, b.ConstructionStartDate, b.ConstructionEndDate,
            b.Phase, b.Location, b.ProjectManagerUserId, b.DocumentFolderStructure, b.DocumentProperties, b.ConstructionProjectManager,
            b.RightOfWayClearDate, b.LinkedSource, b.LinkedProjectNumber), ct);
        return Created($"/api/v1/projects/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] ProjectBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateProjectCommand(
            id, b.Number, b.Name, b.Description, b.Region, b.District, b.OversightType, b.FundingSource, b.FundingAmount,
            b.DesignEstimate, b.LettingDate, b.PlanTurnInDate, b.ConstructionStartDate, b.ConstructionEndDate,
            b.Phase, b.Location, b.ProjectManagerUserId, b.DocumentFolderStructure, b.DocumentProperties, b.ConstructionProjectManager,
            b.RightOfWayClearDate, b.LinkedSource, b.LinkedProjectNumber), ct));

    /// <summary>People who can be chosen as a project's manager: active users of this agency.</summary>
    [HttpGet("managers")]
    [ProducesResponseType(typeof(IReadOnlyList<AssignableManagerDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Managers(CancellationToken ct) => Ok(await _mediator.Send(new ListAssignableManagersQuery(), ct));

    /// <summary>Planned or Build projects a ROW project can be linked to (from the wider platform; a stand-in in the MVP).</summary>
    [HttpGet("catalog")]
    [ProducesResponseType(typeof(IReadOnlyList<CatalogProjectDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Catalog([FromQuery] string source, [FromQuery] string? search, CancellationToken ct) =>
        Ok(await _mediator.Send(new SearchProjectCatalogQuery(source, search), ct));

    /// <summary>Creates many projects from rows prepared elsewhere. Each row succeeds or fails on its own.</summary>
    [HttpPost("import")]
    [ProducesResponseType(typeof(ProjectImportSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Import([FromBody] ImportProjectsCommand command, CancellationToken ct) =>
        Ok(await _mediator.Send(command, ct));

    // ── numbering format ──

    [HttpGet("number-settings")]
    [ProducesResponseType(typeof(ProjectNumberSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNumberSettings(CancellationToken ct) =>
        Ok(await _mediator.Send(new GetProjectNumberSettingsQuery(), ct));

    [HttpPut("number-settings")]
    [ProducesResponseType(typeof(ProjectNumberSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetNumberSettings([FromBody] NumberSettingsBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SetProjectNumberSettingsCommand(b.Pattern, b.FiscalYearStartMonth), ct));

    // ── time zone (a project can override the agency's) ──

    [HttpPut("{id:guid}/time-zone")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetTimeZone(Guid id, [FromBody] TimeZoneBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SetProjectTimeZoneCommand(id, b.TimeZoneId), ct));

    // ── NEPA, exception, gate ──

    [HttpPut("{id:guid}/nepa")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordNepa(Guid id, [FromBody] NepaBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new RecordNepaDecisionCommand(id, b.DecisionType, b.DecisionDate), ct));

    [HttpPut("{id:guid}/acquisition-exception")]
    [ProducesResponseType(typeof(ProjectDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetException(Guid id, [FromBody] ExceptionBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SetAcquisitionExceptionCommand(id, b.ExceptionType, b.ApprovedOn, b.Reason), ct));

    [HttpGet("{id:guid}/acquisition-gate")]
    [ProducesResponseType(typeof(AcquisitionGateDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Gate(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetAcquisitionGateQuery(id), ct));

    // ── cost estimates (versioned) ──

    [HttpGet("{id:guid}/cost-estimates")]
    [ProducesResponseType(typeof(IReadOnlyList<CostEstimateDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListEstimates(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new ListCostEstimatesQuery(id), ct));

    /// <summary>Saves a new estimate version; earlier versions stay.</summary>
    [HttpPost("{id:guid}/cost-estimates")]
    [ProducesResponseType(typeof(CostEstimateDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddEstimate(Guid id, [FromBody] CostEstimateBody b, CancellationToken ct)
    {
        var created = await _mediator.Send(new AddCostEstimateCommand(id, b.LandCost, b.ImprovementsCost, b.RelocationCost, b.IndirectCost, b.Notes), ct);
        return Created($"/api/v1/projects/{id}/cost-estimates", created);
    }

    // ── annual budgets ──

    [HttpGet("{id:guid}/budgets")]
    [ProducesResponseType(typeof(IReadOnlyList<BudgetDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListBudgets(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new ListProjectBudgetsQuery(id), ct));

    [HttpPut("{id:guid}/budgets/{fiscalYear:int}")]
    [ProducesResponseType(typeof(BudgetDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetBudget(Guid id, int fiscalYear, [FromBody] BudgetBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SetProjectBudgetCommand(id, fiscalYear, b.Amount, b.Notes), ct));

    [HttpDelete("{id:guid}/budgets/{fiscalYear:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteBudget(Guid id, int fiscalYear, CancellationToken ct)
    {
        await _mediator.Send(new DeleteProjectBudgetCommand(id, fiscalYear), ct);
        return NoContent();
    }

    // ── ROW authorizations ──

    [HttpGet("{id:guid}/authorizations")]
    [ProducesResponseType(typeof(IReadOnlyList<RowAuthorizationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAuthorizations(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new ListRowAuthorizationsQuery(id), ct));

    [HttpPost("{id:guid}/authorizations")]
    [ProducesResponseType(typeof(RowAuthorizationDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddAuthorization(Guid id, [FromBody] AuthorizationBody b, CancellationToken ct)
    {
        var created = await _mediator.Send(new AddRowAuthorizationCommand(
            id, b.Type, b.AuthorizedAmount, b.FederalSharePercent, b.AuthorizedOn, b.ReferenceNumber, b.ProgrammingConfirmed, b.Notes), ct);
        return Created($"/api/v1/projects/{id}/authorizations", created);
    }

    [HttpPut("{id:guid}/authorizations/{authorizationId:guid}")]
    [ProducesResponseType(typeof(RowAuthorizationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAuthorization(Guid id, Guid authorizationId, [FromBody] AuthorizationBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateRowAuthorizationCommand(
            id, authorizationId, b.Type, b.AuthorizedAmount, b.FederalSharePercent, b.AuthorizedOn, b.ReferenceNumber, b.ProgrammingConfirmed, b.Notes), ct));

    [HttpDelete("{id:guid}/authorizations/{authorizationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAuthorization(Guid id, Guid authorizationId, CancellationToken ct)
    {
        await _mediator.Send(new DeleteRowAuthorizationCommand(id, authorizationId), ct);
        return NoContent();
    }
}
