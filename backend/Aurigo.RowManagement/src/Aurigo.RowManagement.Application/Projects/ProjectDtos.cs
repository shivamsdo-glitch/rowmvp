using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Application.Projects;

public sealed record AcquisitionGateDto(bool Unlocked, string Reason, IReadOnlyList<string> Missing);

public sealed record ProjectSummaryDto(
    Guid Id, string Number, string Name, string? Region, string? District, OversightType OversightType,
    decimal? CurrentEstimate, AcquisitionGateDto AcquisitionGate);

public sealed record ProjectDto(
    Guid Id, string Number, string Name, string? Description, string? Region, string? District,
    OversightType OversightType, string? FundingSource, decimal? FundingAmount, decimal? DesignEstimate,
    decimal? CurrentEstimate,
    DateOnly? LettingDate, DateOnly? PlanTurnInDate, DateOnly? ConstructionStartDate, DateOnly? ConstructionEndDate,
    NepaDecisionType? NepaDecisionType, DateOnly? NepaDecisionDate,
    AcquisitionExceptionType? ExceptionType, DateOnly? ExceptionApprovedOn, string? ExceptionReason,
    AcquisitionGateDto AcquisitionGate, DateTime UpdatedAt,
    /// <summary>The project's own time zone; null means it follows the agency's.</summary>
    string? TimeZoneId = null);

public sealed record CostEstimateDto(
    Guid Id, int Version, decimal LandCost, decimal ImprovementsCost, decimal RelocationCost, decimal IndirectCost,
    decimal Total, string? Notes, DateTime CreatedAt, Guid CreatedBy);

public sealed record BudgetDto(Guid Id, int FiscalYear, decimal Amount, string? Notes, DateTime UpdatedAt);

public sealed record RowAuthorizationDto(
    Guid Id, AuthorizationType Type, decimal AuthorizedAmount, decimal? FederalSharePercent, decimal? FederalShareAmount,
    DateOnly AuthorizedOn, string? ReferenceNumber, bool ProgrammingConfirmed, string? Notes);

public sealed record ProjectNumberSettingsDto(string Pattern, int FiscalYearStartMonth, string Example);
// Example is the number the next project would receive under the format.

public sealed record ProjectImportResultDto(int Row, string Outcome, Guid? ProjectId, string? Number, string? Error);
public sealed record ProjectImportSummaryDto(int Created, int Failed, IReadOnlyList<ProjectImportResultDto> Results);
