using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Application.Mappers;

public static class ProjectMapper
{
    public static AcquisitionGateDto ToDto(this AcquisitionGateResult r) => new(r.Unlocked, r.Reason.ToString(), r.Missing);

    /// <summary>Evaluates the acquisition gate from the facts on a project (ROW-076, 238).</summary>
    public static AcquisitionGateResult EvaluateGate(
        OversightType oversight, NepaDecisionType? nepa, bool hasAcquisitionAuthorization,
        AcquisitionExceptionType? exceptionType, DateOnly? exceptionApprovedOn) =>
        AcquisitionGate.Evaluate(new AcquisitionGateInput(
            oversight, nepa is not null, hasAcquisitionAuthorization, exceptionType is not null && exceptionApprovedOn is not null));

    public static AcquisitionGateResult EvaluateGate(this Project p) =>
        EvaluateGate(p.OversightType, p.NepaDecisionType,
            p.Authorizations.Any(a => AcquisitionGate.CountsForAcquisition(a.Type)), p.ExceptionType, p.ExceptionApprovedOn);

    public static ProjectDto ToDto(this Project p)
    {
        var current = p.Estimates.OrderByDescending(e => e.Version).FirstOrDefault()?.Total;
        return new ProjectDto(
            p.Id, p.Number, p.Name, p.Description, p.Region, p.District, p.OversightType, p.FundingSource,
            p.FundingAmount, p.DesignEstimate, current, p.LettingDate, p.PlanTurnInDate, p.ConstructionStartDate,
            p.ConstructionEndDate, p.NepaDecisionType, p.NepaDecisionDate, p.ExceptionType, p.ExceptionApprovedOn,
            p.ExceptionReason, p.EvaluateGate().ToDto(), p.UpdatedAt, p.TimeZoneId,
            p.Phase, p.Location, p.ProjectManagerUserId, p.ProjectManagerName, p.DocumentFolderStructure, p.DocumentProperties,
            p.ConstructionProjectManager, p.RightOfWayClearDate, p.LinkedSource, p.LinkedProjectNumber, p.LinkedProjectName,
            p.Budgets.Sum(b => b.Amount), 0m);
    }

    public static CostEstimateDto ToDto(this ProjectCostEstimate e) => new(
        e.Id, e.Version, e.LandCost, e.ImprovementsCost, e.RelocationCost, e.IndirectCost, e.Total, e.Notes, e.CreatedAt, e.CreatedBy);

    public static BudgetDto ToDto(this ProjectBudget b) => new(b.Id, b.FiscalYear, b.Amount, b.Notes, b.UpdatedAt);

    public static RowAuthorizationDto ToDto(this RowAuthorization a) => new(
        a.Id, a.Type, a.AuthorizedAmount, a.FederalSharePercent,
        a.FederalSharePercent is { } pct ? Math.Round(a.AuthorizedAmount * pct / 100m, 2) : null,
        a.AuthorizedOn, a.ReferenceNumber, a.ProgrammingConfirmed, a.Notes);
}
