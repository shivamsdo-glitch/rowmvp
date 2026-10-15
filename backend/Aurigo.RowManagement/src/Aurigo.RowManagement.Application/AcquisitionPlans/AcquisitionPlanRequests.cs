using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.AcquisitionPlans;

public sealed record AcquisitionPlanDto(Guid ProjectId, string ProjectNumber, string ProjectName, DateOnly Today, PlanSummary Plan);

/// <summary>The project's ROW acquisition plan and schedule, computed from its live parcels, budget and latest estimate (ROW-241).</summary>
public sealed record GetAcquisitionPlanQuery(Guid ProjectId) : IRequest<AcquisitionPlanDto>;

public sealed class GetAcquisitionPlanQueryHandler : IRequestHandler<GetAcquisitionPlanQuery, AcquisitionPlanDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public GetAcquisitionPlanQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<AcquisitionPlanDto> Handle(GetAcquisitionPlanQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == request.ProjectId, ct) ?? throw new NotFoundException("Project", request.ProjectId);
        var today = await _parcels.TodayForProjectAsync(project.Id, ct);

        var parcels = await _db.Parcels.Where(p => p.ProjectId == project.Id && p.State == ParcelState.Active).ToListAsync(ct);
        var parcelIds = parcels.Select(p => p.Id).ToList();
        var milestones = await _db.ParcelMilestones.Where(m => parcelIds.Contains(m.ParcelId)).ToListAsync(ct);
        var statuses = await _db.ParcelStatuses.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var stepLabels = new Dictionary<AcquisitionType, Dictionary<string, string>>();
        foreach (var type in parcels.Select(p => p.AcquisitionType).Distinct())
            stepLabels[type] = (await _parcels.TemplateAsync(type, ct)).Steps.ToDictionary(s => s.Key, s => s.Label);

        var inputs = parcels.Select(p => new PlanParcelInput(
            p.Id, p.Number, p.Address, p.AreaSqFt, statuses.GetValueOrDefault(p.StatusId, "Unknown"),
            stepLabels[p.AcquisitionType].GetValueOrDefault(p.CurrentStepKey, p.CurrentStepKey),
            milestones.Where(m => m.ParcelId == p.Id).Select(m => new PlanMilestone(m.Name, m.DueOn, m.CompletedOn)).ToList())).ToList();

        var budgets = (await _db.ProjectBudgets.Where(b => b.ProjectId == project.Id).ToListAsync(ct)).Select(b => new PlanBudgetYear(b.FiscalYear, b.Amount)).ToList();
        var latest = (await _db.ProjectCostEstimates.Where(e => e.ProjectId == project.Id).ToListAsync(ct)).OrderByDescending(e => e.Version).FirstOrDefault();
        var authorized = (await _db.RowAuthorizations.Where(a => a.ProjectId == project.Id).Select(a => a.AuthorizedAmount).ToListAsync(ct)).Sum();

        return new AcquisitionPlanDto(project.Id, project.Number, project.Name, today, AcquisitionPlan.Build(inputs, budgets, latest?.Total, authorized, today));
    }
}
