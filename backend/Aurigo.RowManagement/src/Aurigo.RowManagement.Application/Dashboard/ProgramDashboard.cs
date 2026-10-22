using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Parties;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Dashboard;

// ── The ROW Director's view (ADR 0024): where every project stands, what is going on with each parcel, and which team the time goes to ──
// Computed per request from the live records and the workflow history. Nothing is stored. Project managers and administrators only.

public sealed record ProgramTileDto(string Key, string Label, decimal Value, string Format, string Tone);
public sealed record StepCountDto(string StepKey, string Label, int Count);
public sealed record StageDto(string StepKey, string Label, string Team, int ParcelsNow, int CompletedStays, double? AvgDaysCompleted, double? AvgDaysOpen, double? LongestOpenDays, int Stuck);
public sealed record TeamTimeDto(string Team, int ParcelsNow, int Stays, double? AvgDaysPerStay, double TotalDays, double? LongestOpenDays, int Stuck);
public sealed record StuckParcelDto(Guid ParcelId, string ParcelNumber, Guid ProjectId, string ProjectNumber, string StepLabel, string Team, double Days);

public sealed record ProjectRowDto(
    Guid Id, string Number, string Name, string? Phase, string? ProjectManager, string OversightType,
    int Parcels, int Closed, int PercentClosed, IReadOnlyList<StepCountDto> ByStep, int OverdueParcels, int StuckParcels,
    decimal? EstimateTotal, decimal KnownCost, DateOnly? RowClearDate, int? DaysToClear, string Health, IReadOnlyList<string> Reasons,
    int Displaced = 0, int Relocated = 0, int RelocationsOpen = 0);

public sealed record ParcelRowDto(
    Guid Id, string Number, string? Address, string? OwnerName, string Status, string StatusColor, string StepKey, string StepLabel, string Team, double DaysInStep,
    decimal? AppraisalTotal, decimal? ApprovedJustCompensation, decimal? AgreedAmount, DateOnly? AnticipatedClosing, DateOnly? ActualClosing, bool Closed, IReadOnlyList<string> Flags,
    string? Relocation = null, int Displaced = 0, int Relocated = 0);

public sealed record ProgramDashboardDto(
    DateTime GeneratedAt, int StuckDays, ProgramRulesDto Rules, IReadOnlyList<ProgramTileDto> Tiles, IReadOnlyList<ProjectRowDto> Projects, IReadOnlyList<StageDto> Stages,
    IReadOnlyList<TeamTimeDto> Teams, IReadOnlyList<StuckParcelDto> Stuck);

public sealed record ProjectDashboardDto(
    DateTime GeneratedAt, int StuckDays, ProgramRulesDto Rules, ProjectRowDto Project, IReadOnlyList<StageDto> Stages, IReadOnlyList<TeamTimeDto> Teams, IReadOnlyList<ParcelRowDto> Parcels);

internal sealed class ProgramBuilder
{
    private readonly IRowDbContext _db;
    private readonly IMediator _mediator;
    private readonly TimeProvider _clock;

    public ProgramBuilder(IRowDbContext db, IMediator mediator, TimeProvider clock)
    {
        _db = db;
        _mediator = mediator;
        _clock = clock;
    }

    public sealed record Built(
        DateTime Now, ProgramRules Rules, IReadOnlyList<ProjectRowDto> Projects, IReadOnlyDictionary<Guid, List<ParcelRowDto>> ParcelsByProject, IReadOnlyList<StageDto> Stages,
        IReadOnlyList<TeamTimeDto> Teams, IReadOnlyList<StuckParcelDto> Stuck);

    private sealed record P(Guid Id, Guid ProjectId, string Number, string? Address, Guid StatusId, string CurrentStepKey, DateTime CreatedAt);

    public async Task<Built> BuildAsync(Guid? projectId, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var projects = await _db.Projects.AsNoTracking().Where(p => projectId == null || p.Id == projectId).OrderBy(p => p.Number).ToListAsync(ct);
        if (projectId is { } wanted && projects.Count == 0) throw new NotFoundException("Project", wanted);
        var projectIds = projects.Select(p => p.Id).ToList();

        var parcels = await _db.Parcels.AsNoTracking().Where(p => projectIds.Contains(p.ProjectId) && p.State == ParcelState.Active)
            .Select(p => new P(p.Id, p.ProjectId, p.Number, p.Address, p.StatusId, p.CurrentStepKey, p.CreatedAt)).ToListAsync(ct);
        var parcelIds = parcels.Select(p => p.Id).ToList();

        // The agency's thresholds and step-to-team choices, and the workflow: the standard steps in order, then steps only another acquisition type has.
        var (rules, chosenTeams) = await ProgramConfig.LoadAsync(_db, ct);
        var allSteps = await ProgramConfig.StepsAsync(_db, ct);
        var templates = await _db.WorkflowTemplates.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.AcquisitionType, ct);
        var standardKeys = (await _db.WorkflowSteps.AsNoTracking().Select(s => new { s.TemplateId, s.Key, s.Order }).ToListAsync(ct))
            .Where(s => templates.GetValueOrDefault(s.TemplateId) == AcquisitionType.Standard).OrderBy(s => s.Order).Select(s => s.Key).Distinct().ToList();
        var extraKeys = allSteps.Select(s => s.Key).Where(k => !standardKeys.Contains(k)).ToList();
        var labels = allSteps.ToDictionary(s => s.Key, s => s.Label);
        string Label(string key) => labels.GetValueOrDefault(key, key);
        string TeamOf(string key) => StageTimes.TeamOf(key, chosenTeams);

        var events = (await _db.ParcelStepEvents.AsNoTracking().Where(e => parcelIds.Contains(e.ParcelId)).Select(e => new { e.ParcelId, e.FromStepKey, e.ToStepKey, e.CreatedAt }).ToListAsync(ct))
            .ToLookup(e => e.ParcelId);
        var histories = parcels.ToDictionary(p => p.Id, p => new ParcelStepHistory(p.Id, p.CurrentStepKey, p.CreatedAt,
            events[p.Id].Select(e => new StepMove(e.FromStepKey, e.ToStepKey, e.CreatedAt)).ToList()));

        var statuses = await _db.ParcelStatuses.AsNoTracking().ToDictionaryAsync(s => s.Id, s => new { s.Name, s.Color }, ct);
        var owners = (await ParcelOwners.NamesAsync(_db, parcelIds, null, ct)).ToDictionary(o => o.Key, o => string.Join("; ", o.Value.Take(2)) + (o.Value.Count > 2 ? "…" : ""));
        var closedMilestone = (await _db.ParcelMilestones.AsNoTracking().Where(m => parcelIds.Contains(m.ParcelId) && m.Name == MilestoneNames.Closed && m.CompletedOn != null).Select(m => m.ParcelId).ToListAsync(ct)).ToHashSet();
        var closings = await _db.ParcelTitleClosings.AsNoTracking().Where(t => parcelIds.Contains(t.ParcelId)).ToDictionaryAsync(t => t.ParcelId, t => new { t.AnticipatedClosingDate, t.ActualClosingDate }, ct);
        var negotiated = await _db.ParcelNegotiations.AsNoTracking().Where(n => parcelIds.Contains(n.ParcelId)).ToDictionaryAsync(n => n.ParcelId, n => n.TotalAgreementAmount, ct);
        var appraisals = (await _db.ParcelAppraisals.AsNoTracking().Where(a => parcelIds.Contains(a.ParcelId))
                .Select(a => new { a.ParcelId, a.Sequence, a.ApprovedJustCompensation, Total = a.Amounts.Sum(x => x.Amount) }).ToListAsync(ct))
            .GroupBy(a => a.ParcelId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Sequence).ToList());
        var estimates = (await _db.ProjectCostEstimates.AsNoTracking().Where(e => projectIds.Contains(e.ProjectId)).Select(e => new { e.ProjectId, e.Version, e.Total }).ToListAsync(ct))
            .GroupBy(e => e.ProjectId).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Version).First().Total);
        var overdue = (await _mediator.Send(new ListRemindersQuery(0), ct)).Where(r => r.Status == nameof(DeadlineStatus.Overdue)).Select(r => r.ParcelId).ToHashSet();

        // Relocation (ADR 0026): per parcel, the displaced ownership interests and how many have finished.
        var tractOf = await _db.Parcels.AsNoTracking().Where(x => parcelIds.Contains(x.Id)).Select(x => new { x.Id, x.ParentTractId }).ToDictionaryAsync(x => x.Id, x => x.ParentTractId, ct);
        var tractIds = tractOf.Values.Where(t => t != null).Select(t => t!.Value).Distinct().ToList();
        var flagged = (await _db.PartyLinks.AsNoTracking().Where(l => l.DisplacementRequired && ((l.ParcelId != null && parcelIds.Contains(l.ParcelId.Value)) || (l.ParentTractId != null && tractIds.Contains(l.ParentTractId.Value))))
            .Select(l => new { l.Id, l.ParcelId, l.ParentTractId, l.Role }).ToListAsync(ct)).Where(l => PartyRoles.CategoryOf(l.Role) == PartyCategory.Ownership).ToList();
        var relocs = (await _db.ParcelRelocations.AsNoTracking().Where(r => parcelIds.Contains(r.ParcelId))
            .Select(r => new { r.ParcelId, r.PartyLinkId, r.RelocationComplete, r.IneligibleForRelocation, r.ActualVacateDate }).ToListAsync(ct)).ToLookup(r => r.ParcelId);

        var parcelRows = new Dictionary<Guid, List<ParcelRowDto>>();
        var projectRows = new List<ProjectRowDto>();
        foreach (var project in projects)
        {
            var mine = parcels.Where(p => p.ProjectId == project.Id).OrderBy(p => p.Number, StringComparer.OrdinalIgnoreCase).ToList();
            var rows = new List<ParcelRowDto>();
            decimal known = 0;
            foreach (var p in mine)
            {
                var open = StageTimes.Stays(histories[p.Id]).LastOrDefault(s => s.IsOpen);
                var days = open?.DaysAt(now) ?? 0;
                var apps = appraisals.GetValueOrDefault(p.Id);
                var approved = apps?.FirstOrDefault(a => a.ApprovedJustCompensation != null)?.ApprovedJustCompensation;
                var agreed = negotiated.GetValueOrDefault(p.Id);
                var closing = closings.GetValueOrDefault(p.Id);
                var isClosed = closedMilestone.Contains(p.Id) || closing?.ActualClosingDate != null;
                known += agreed ?? approved ?? 0;
                var flags = new List<string>();
                if (overdue.Contains(p.Id)) flags.Add("Overdue deadline");
                if (!isClosed && days > rules.StuckDays) flags.Add($"{Math.Floor(days):0} days on this step");
                if (!isClosed && closing is { AnticipatedClosingDate: { } due, ActualClosingDate: null } && due < today) flags.Add("Closing past due");
                var interests = flagged.Where(l => l.ParcelId == p.Id || (tractOf.GetValueOrDefault(p.Id) is { } tid && l.ParentTractId == tid)).Select(l => l.Id).ToList();
                var rel = RelocationCalc.Progress(interests, relocs[p.Id].Select(r => new RelocationProgressInput(r.PartyLinkId, r.RelocationComplete)));
                if (relocs[p.Id].Any(r => RelocationCalc.ClaimExpiring(r.ActualVacateDate, r.RelocationComplete, today, 60))) flags.Add("Relocation claim period ending");
                var relocationText = rel.Flagged == 0 ? (relocs[p.Id].Any() ? $"{relocs[p.Id].Count()} case(s)" : null) : $"{rel.Completed} of {rel.Flagged} relocated";
                var status = statuses.GetValueOrDefault(p.StatusId);
                rows.Add(new ParcelRowDto(p.Id, p.Number, p.Address, owners.GetValueOrDefault(p.Id), status?.Name ?? "—", status?.Color ?? "#64748b", p.CurrentStepKey, Label(p.CurrentStepKey),
                    TeamOf(p.CurrentStepKey), Math.Round(days, 1), apps?.FirstOrDefault()?.Total, approved, agreed, closing?.AnticipatedClosingDate, closing?.ActualClosingDate, isClosed, flags,
                    relocationText, rel.Flagged, rel.Completed));
            }
            parcelRows[project.Id] = rows;

            var closed = rows.Count(r => r.Closed);
            var overdueCount = rows.Count(r => r.Flags.Contains("Overdue deadline"));
            var stuckCount = rows.Count(r => !r.Closed && r.DaysInStep > rules.StuckDays);
            estimates.TryGetValue(project.Id, out var estimate);
            var health = ProjectHealth.Evaluate(new ProjectHealthInput(rows.Count, closed, overdueCount, stuckCount, project.RightOfWayClearDate, today, estimates.ContainsKey(project.Id) ? estimate : null, known), rules);
            projectRows.Add(new ProjectRowDto(
                project.Id, project.Number, project.Name, project.Phase, project.ProjectManagerName, project.OversightType.ToString(),
                rows.Count, closed, rows.Count == 0 ? 0 : (int)Math.Floor(closed * 100.0 / rows.Count),
                standardKeys.Concat(extraKeys).Select(k => new StepCountDto(k, Label(k), rows.Count(r => r.StepKey == k))).Where(c => c.Count > 0 || standardKeys.Contains(c.StepKey)).ToList(), overdueCount, stuckCount,
                estimates.ContainsKey(project.Id) ? estimate : null, known, project.RightOfWayClearDate,
                project.RightOfWayClearDate is { } d ? d.DayNumber - today.DayNumber : null, health.Level.ToString(), health.Reasons,
                rows.Sum(r => r.Displaced), rows.Sum(r => r.Relocated), mine.Sum(p => relocs[p.Id].Count(r => r.RelocationComplete == null && !r.IneligibleForRelocation))));
        }

        var all = histories.Values.ToList();
        var stages = StageTimes.Summarize(all, standardKeys, now, rules.StuckDays, chosenTeams).Select(s => new StageDto(s.StepKey, Label(s.StepKey), s.Team, s.ParcelsNow, s.CompletedStays,
            s.AvgDaysCompleted is { } a ? Math.Round(a, 1) : null, s.AvgDaysOpen is { } o ? Math.Round(o, 1) : null, s.LongestOpenDays is { } l ? Math.Round(l, 1) : null, s.Stuck)).ToList();
        var teams = StageTimes.ByTeam(all, now, rules.StuckDays, chosenTeams).Select(t => new TeamTimeDto(t.Team, t.ParcelsNow, t.Stays, t.AvgDaysPerStay is { } a ? Math.Round(a, 1) : null, Math.Round(t.TotalDays, 1),
            t.LongestOpenDays is { } l ? Math.Round(l, 1) : null, t.Stuck)).ToList();

        var number = projects.ToDictionary(p => p.Id, p => p.Number);
        var parcelById = parcels.ToDictionary(p => p.Id);
        var closedIds = parcelRows.Values.SelectMany(r => r).Where(r => r.Closed).Select(r => r.Id).ToHashSet();
        var stuck = all.SelectMany(StageTimes.Stays).Where(s => s.IsOpen && !closedIds.Contains(s.ParcelId) && s.DaysAt(now) > rules.StuckDays)
            .OrderByDescending(s => s.DaysAt(now)).Take(10)
            .Select(s => new StuckParcelDto(s.ParcelId, parcelById[s.ParcelId].Number, parcelById[s.ParcelId].ProjectId, number[parcelById[s.ParcelId].ProjectId], Label(s.StepKey), TeamOf(s.StepKey), Math.Round(s.DaysAt(now), 1))).ToList();

        return new Built(now, rules, projectRows, parcelRows, stages, teams, stuck);
    }
}

internal static class ProgramAccess
{
    public static async Task EnsureDirectorAsync(IPagePermissionService permissions, ICurrentUser user, CancellationToken ct)
    {
        await permissions.EnsureAsync(PageKeys.Dashboard, PermissionAction.Read, ct);
        if (!DashboardShared.IsManager(user)) throw new ForbiddenException("The program view is for project managers and administrators.");
    }
}

/// <summary>Every project with its health, stage counts and cost, plus where the time goes across the whole program.</summary>
public sealed record GetProgramDashboardQuery : IRequest<ProgramDashboardDto>;

public sealed class GetProgramDashboardQueryHandler : IRequestHandler<GetProgramDashboardQuery, ProgramDashboardDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentUser _user;
    private readonly IMediator _mediator;
    private readonly TimeProvider _clock;

    public GetProgramDashboardQueryHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentUser user, IMediator mediator, TimeProvider clock)
    {
        _db = db;
        _permissions = permissions;
        _user = user;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task<ProgramDashboardDto> Handle(GetProgramDashboardQuery request, CancellationToken ct)
    {
        await ProgramAccess.EnsureDirectorAsync(_permissions, _user, ct);
        var b = await new ProgramBuilder(_db, _mediator, _clock).BuildAsync(null, ct);
        var parcels = b.Projects.Sum(p => p.Parcels);
        var closed = b.Projects.Sum(p => p.Closed);
        var attention = b.Projects.Count(p => p.Health is "Red" or "Amber");
        var tiles = new List<ProgramTileDto>
        {
            new("projects", "Projects", b.Projects.Count, "count", "info"),
            new("parcels", "Active parcels", parcels, "count", "info"),
            new("closed", "Parcels closed", parcels == 0 ? 0 : Math.Floor(closed * 100m / parcels), "percent", "info"),
            new("attention", "Projects needing attention", attention, "count", attention > 0 ? "warn" : "info"),
            new("stuck", "Parcels stuck on a step", b.Projects.Sum(p => p.StuckParcels), "count", b.Projects.Any(p => p.StuckParcels > 0) ? "danger" : "info"),
            new("relocating", "Displaced interests relocated", b.Projects.Sum(p => p.Relocated), "count", "info"),
            new("estimate", "Estimated ROW cost", b.Projects.Sum(p => p.EstimateTotal ?? 0), "money", "info"),
            new("known", "Known cost to date", b.Projects.Sum(p => p.KnownCost), "money", "info"),
        };
        return new ProgramDashboardDto(b.Now, b.Rules.StuckDays, ProgramConfig.ToDto(b.Rules), tiles, b.Projects, b.Stages, b.Teams, b.Stuck);
    }
}

/// <summary>One project in depth: its health, its stages and every parcel with what is going on.</summary>
public sealed record GetProjectDashboardQuery(Guid ProjectId) : IRequest<ProjectDashboardDto>;

public sealed class GetProjectDashboardQueryHandler : IRequestHandler<GetProjectDashboardQuery, ProjectDashboardDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentUser _user;
    private readonly IMediator _mediator;
    private readonly TimeProvider _clock;

    public GetProjectDashboardQueryHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentUser user, IMediator mediator, TimeProvider clock)
    {
        _db = db;
        _permissions = permissions;
        _user = user;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task<ProjectDashboardDto> Handle(GetProjectDashboardQuery request, CancellationToken ct)
    {
        await ProgramAccess.EnsureDirectorAsync(_permissions, _user, ct);
        var b = await new ProgramBuilder(_db, _mediator, _clock).BuildAsync(request.ProjectId, ct);
        var project = b.Projects.Single();
        return new ProjectDashboardDto(b.Now, b.Rules.StuckDays, ProgramConfig.ToDto(b.Rules), project, b.Stages, b.Teams, b.ParcelsByProject[project.Id]);
    }
}
