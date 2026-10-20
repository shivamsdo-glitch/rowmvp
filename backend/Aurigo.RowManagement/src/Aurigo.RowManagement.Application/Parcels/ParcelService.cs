using System.Collections.Concurrent;
using System.Text.Json;
using Aurigo.RowManagement.Application.Agency;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Mappers;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Parcels;

/// <summary>
/// The shared plumbing every parcel command and query uses: loading with its children, the reference checks (status, group,
/// tract), the workflow and gate lookups, and the detail ("synopsis") view of a parcel (ROW-005, 009, 010, 072, 073).
/// </summary>
public sealed class ParcelService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> NumberLocks = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IRowDbContext _db;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;
    private readonly AgencyClock _agency;

    public ParcelService(IRowDbContext db, ICurrentUser user, TimeProvider clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _agency = new AgencyClock(db, clock);
    }

    /// <summary>The instant, for timestamps. Calendar days are counted per project with <see cref="TodayForProjectAsync"/>.</summary>
    public DateTime Now => _clock.GetUtcNow().UtcDateTime;
    public AgencyClock Agency => _agency;

    /// <summary>The calendar day it is where this project is (its time zone, else the agency's).</summary>
    public Task<DateOnly> TodayForProjectAsync(Guid projectId, CancellationToken ct) => _agency.TodayForProjectAsync(projectId, ct);

    // ── loading ──────────────────────────────────────────────────────────

    public async Task<Parcel> LoadAsync(Guid id, CancellationToken ct) =>
        await _db.Parcels.Include(p => p.Interests).Include(p => p.Milestones).Include(p => p.SiteVisits).Include(p => p.StepEvents)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new NotFoundException("Parcel", id);

    public async Task<Parcel> LoadActiveAsync(Guid id, CancellationToken ct)
    {
        var parcel = await LoadAsync(id, ct);
        if (parcel.State != ParcelState.Active)
            throw new ConflictException("parcel_not_active", $"Parcel {parcel.Number} was {parcel.State.ToString().ToLowerInvariant()} and can no longer be changed.");
        return parcel;
    }

    public async Task<Project> LoadProjectAsync(Guid projectId, CancellationToken ct) =>
        await _db.Projects.Include(p => p.Authorizations).FirstOrDefaultAsync(p => p.Id == projectId, ct)
        ?? throw new NotFoundException("Project", projectId);

    // ── reference checks ─────────────────────────────────────────────────

    public async Task<ParcelStatus> InitialStatusAsync(CancellationToken ct) =>
        await _db.ParcelStatuses.Where(s => s.IsActive && s.IsInitial).OrderBy(s => s.SortOrder).FirstOrDefaultAsync(ct)
        ?? await _db.ParcelStatuses.Where(s => s.IsActive).OrderBy(s => s.SortOrder).FirstOrDefaultAsync(ct)
        ?? throw new BusinessRuleException("No parcel status is set up. An administrator must add one in Configuration.");

    /// <summary>The status must exist and be selectable (active). Re-choosing a parcel's current inactive status is the caller's call.</summary>
    public async Task<ParcelStatus> SelectableStatusAsync(Guid id, CancellationToken ct)
    {
        var status = await _db.ParcelStatuses.FirstOrDefaultAsync(s => s.Id == id, ct)
                     ?? throw new InvalidRequestException("invalid_status", "That parcel status does not exist.");
        if (!status.IsActive) throw new InvalidRequestException("invalid_status", $"The status '{status.Name}' is no longer in use.");
        return status;
    }

    public async Task EnsureGroupAsync(Guid? groupId, Guid projectId, CancellationToken ct)
    {
        if (groupId is null) return;
        var group = await _db.ParcelGroups.FirstOrDefaultAsync(g => g.Id == groupId, ct)
                    ?? throw new InvalidRequestException("invalid_group", "That parcel group does not exist.");
        if (group.ProjectId != projectId) throw new InvalidRequestException("invalid_group", "That parcel group belongs to a different project.");
    }

    public async Task EnsureTractAsync(Guid? tractId, Guid projectId, CancellationToken ct)
    {
        if (tractId is null) return;
        var tract = await _db.ParentTracts.FirstOrDefaultAsync(t => t.Id == tractId, ct)
                    ?? throw new InvalidRequestException("invalid_tract", "That parent tract does not exist.");
        if (tract.ProjectId != projectId) throw new InvalidRequestException("invalid_tract", "That parent tract belongs to a different project.");
    }

    public async Task EnsureNumberFreeAsync(Guid projectId, string number, Guid? exceptParcelId, CancellationToken ct)
    {
        var lower = number.ToLower();
        if (await _db.Parcels.AnyAsync(p => p.ProjectId == projectId && p.Number.ToLower() == lower && p.Id != exceptParcelId, ct))
            throw new ConflictException("parcel_number_exists", $"This project already has a parcel numbered '{number}'.");
    }

    // ── workflow, gate, rules ────────────────────────────────────────────

    public async Task<WorkflowTemplate> TemplateAsync(AcquisitionType type, CancellationToken ct) =>
        await _db.WorkflowTemplates.Include(t => t.Steps).FirstOrDefaultAsync(t => t.AcquisitionType == type, ct)
        ?? throw new BusinessRuleException($"No workflow is set up for {type} acquisitions.");

    public static IReadOnlyList<WorkflowStepInfo> StepInfos(WorkflowTemplate t) =>
        t.Steps.OrderBy(s => s.Order).Select(s => new WorkflowStepInfo(s.Key, s.Label, s.RequiresAcquisitionGate)).ToList();

    public async Task<bool> CanOverrideAsync(CancellationToken ct)
    {
        var extra = (await _db.StepGateSettings.FirstOrDefaultAsync(ct))?.ExtraOverrideRoles ?? new List<string>();
        return StepGate.MayOverride(_user.Roles, extra);
    }

    public async Task<IReadOnlyList<CustomFieldInfo>> CustomFieldInfosAsync(CancellationToken ct) =>
        (await _db.CustomFieldDefinitions.Where(d => d.EntityType == "Parcel").OrderBy(d => d.SortOrder).ToListAsync(ct))
            .Select(d => new CustomFieldInfo(d.Key, d.Label, d.FieldType, d.Options, d.IsRequired, d.IsActive)).ToList();

    public static Dictionary<string, string> ReadCustomValues(string json) =>
        string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(json, Json) ?? new();

    public static string WriteCustomValues(IReadOnlyDictionary<string, string> values) => JsonSerializer.Serialize(values, Json);

    /// <summary>Validates submitted custom values (null = leave as is) and returns the JSON to store.</summary>
    public async Task<string> CustomValuesJsonAsync(IReadOnlyDictionary<string, string?>? submitted, string existingJson, CancellationToken ct)
    {
        var existing = ReadCustomValues(existingJson);
        var check = CustomFieldValidation.Validate(await CustomFieldInfosAsync(ct), submitted ?? existing.ToDictionary(k => k.Key, k => (string?)k.Value), existing);
        if (!check.IsValid) throw new BusinessRuleException(string.Join(" ", check.Errors));
        return WriteCustomValues(check.Values);
    }

    // ── creation helpers ─────────────────────────────────────────────────

    /// <summary>The standard milestones every parcel starts with; "Parcel set up" is complete from the day it is created.</summary>
    public void AddStandardMilestones(Parcel parcel, DateOnly today)
    {
        foreach (var name in MilestoneNames.Standard)
            parcel.Milestones.Add(new ParcelMilestone
            {
                ParcelId = parcel.Id,
                Name = name,
                CompletedOn = name == MilestoneNames.ParcelSetUp ? today : null,
            });
    }

    /// <summary>Serialises number generation for one project within this server; the unique index still guards other servers.</summary>
    public static SemaphoreSlim NumberLock(Guid projectId) => NumberLocks.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));

    public async Task<string> NextNumberAsync(Guid projectId, CancellationToken ct) =>
        ParcelSplit.NextNumber(await _db.Parcels.Where(p => p.ProjectId == projectId).Select(p => p.Number).ToListAsync(ct));

    // ── views ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ParcelSummaryDto>> SummariesAsync(IReadOnlyCollection<Parcel> parcels, CancellationToken ct)
    {
        if (parcels.Count == 0) return Array.Empty<ParcelSummaryDto>();
        var statusIds = parcels.Select(p => p.StatusId).Distinct().ToList();
        var groupIds = parcels.Where(p => p.GroupId != null).Select(p => p.GroupId!.Value).Distinct().ToList();
        var tractIds = parcels.Where(p => p.ParentTractId != null).Select(p => p.ParentTractId!.Value).Distinct().ToList();
        var projectIds = parcels.Select(p => p.ProjectId).Distinct().ToList();

        var statuses = await _db.ParcelStatuses.Where(s => statusIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var groups = await _db.ParcelGroups.Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var tracts = await _db.ParentTracts.Where(t => tractIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Number, ct);
        var projects = await _db.Projects.Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, ct);

        return parcels.Select(p =>
        {
            statuses.TryGetValue(p.StatusId, out var st);
            return new ParcelSummaryDto(
                p.Id, p.ProjectId, projects.GetValueOrDefault(p.ProjectId, "?"), p.Number, p.Address, p.AreaSqFt, p.PropertyType,
                p.StatusId, st?.Name ?? "Unknown", st?.Color ?? "#64748b",
                p.GroupId, p.GroupId is { } g ? groups.GetValueOrDefault(g) : null,
                p.ParentTractId, p.ParentTractId is { } t ? tracts.GetValueOrDefault(t) : null,
                p.AcquisitionType, p.CurrentStepKey, p.IsRemnant, p.State);
        }).ToList();
    }

    public async Task<ParcelDto> ToDtoAsync(Parcel p, CancellationToken ct)
    {
        var project = await LoadProjectAsync(p.ProjectId, ct);
        var status = await _db.ParcelStatuses.FirstOrDefaultAsync(s => s.Id == p.StatusId, ct);
        var group = p.GroupId is { } gid ? await _db.ParcelGroups.FirstOrDefaultAsync(g => g.Id == gid, ct) : null;
        var tract = p.ParentTractId is { } tid ? await _db.ParentTracts.FirstOrDefaultAsync(t => t.Id == tid, ct) : null;

        var template = await TemplateAsync(p.AcquisitionType, ct);
        var steps = StepInfos(template);
        var stepDtos = template.Steps.OrderBy(s => s.Order).Select(s => new WorkflowStepDto(s.Key, s.Label, s.Order, s.RequiresAcquisitionGate)).ToList();
        var gateOpen = project.EvaluateGate().Unlocked;
        var workflow = new WorkflowStateDto(stepDtos, p.CurrentStepKey, StepGate.NextStepKey(steps, p.CurrentStepKey), gateOpen, await CanOverrideAsync(ct));

        var zone = await _agency.ZoneOfAsync(project, ct);
        var today = AgencyTime.TodayIn(zone, _agency.UtcNow);
        var rules = await _db.DeadlineRules.Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync(ct);
        var milestoneInfos = p.Milestones.Select(m => new MilestoneInfo(m.Name, m.DueOn, m.CompletedOn)).ToList();
        var deadlines = StatutoryDeadlines.Compute(
                rules.Select(r => new DeadlineRuleInfo(r.Id, r.Name, r.TriggerMilestone, r.TargetMilestone, r.Days)), milestoneInfos, today)
            .Select(d => new DeadlineDto(d.Rule.Id, d.Rule.Name, d.Rule.TriggerMilestone, d.Rule.TargetMilestone, d.Rule.Days,
                d.TriggerDate, d.DueOn, d.Status.ToString(), d.DaysRemaining))
            .ToList();

        var standardOrder = MilestoneNames.Standard.ToList();
        var milestones = p.Milestones
            .OrderBy(m => standardOrder.IndexOf(m.Name) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(m => m.Name)
            .Select(m => new ParcelMilestoneDto(m.Id, m.Name, m.DueOn, m.CompletedOn, m.Notes, standardOrder.Contains(m.Name),
                m.CompletedOn is null && m.DueOn is { } due && due < today))
            .ToList();

        var owners = await _db.ParcelOwnerships.Where(o => o.ParcelId == p.Id).OrderBy(o => o.Sequence).Select(o => o.OwnerName).ToListAsync(ct);
        var ownerName = owners.Count == 0 ? null : string.Join("; ", owners);

        return new ParcelDto(
            p.Id, p.ProjectId, project.Number, project.Name, p.Number, p.LegalDescription, p.Address, p.AreaSqFt, p.PropertyType,
            p.StatusId, status?.Name ?? "Unknown", status?.Color ?? "#64748b", p.GroupId, group?.Name, p.ParentTractId, tract?.Number,
            tract?.TaxParcelNumbers ?? new List<string>(), p.AcquisitionType, p.State, p.SupersededOn, p.SourceParcelIds, p.IsRemnant,
            ReadCustomValues(p.CustomValuesJson), p.NoticeIssuedOn, p.NoticeMethod,
            p.Interests.OrderBy(i => i.Type).ThenBy(i => i.CreatedAt).Select(i => new ParcelInterestDto(i.Id, i.Type, i.AreaSqFt, i.Notes)).ToList(),
            milestones,
            p.SiteVisits.OrderByDescending(v => v.VisitedOn).Select(v => new ParcelSiteVisitDto(v.Id, v.VisitedOn, v.Notes, v.OwnerContactConfirmed)).ToList(),
            deadlines, workflow,
            p.StepEvents.OrderByDescending(e => e.CreatedAt).Select(e => new ParcelStepEventDto(e.Id, e.FromStepKey, e.ToStepKey, e.IsOverride, e.Reason, e.CreatedAt, e.CreatedBy)).ToList(),
            p.UpdatedAt, zone, p.CadNumber, p.AcquisitionInterestType, p.PossessionDate, p.ProposedPossessionDate, p.Segment, p.Priority,
            p.PossessionType, p.ParcelLocation, p.IsActiveParcel, ownerName);
    }
}
