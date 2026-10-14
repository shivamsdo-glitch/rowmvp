using System.Text.RegularExpressions;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Parcels;

// Reading the rules needs Read on Parcels (every parcel screen uses them); changing them needs Update on Administration.

internal static class ConfigRules
{
    public static readonly Regex Color = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    public static readonly Regex Key = new(@"^[a-z][a-z0-9_]{0,39}$", RegexOptions.Compiled);

    public static ParcelStatusDto ToDto(this ParcelStatus s) => new(s.Id, s.Name, s.Color, s.SortOrder, s.IsActive, s.IsInitial);

    public static CustomFieldDto ToDto(this CustomFieldDefinition d) =>
        new(d.Id, d.EntityType, d.Key, d.Label, d.FieldType, d.Options, d.IsRequired, d.IsActive, d.SortOrder);

    public static DeadlineRuleDto ToDto(this DeadlineRule r) => new(r.Id, r.Name, r.TriggerMilestone, r.TargetMilestone, r.Days, r.IsActive, r.IsPlaceholder);
}

// ── Parcel statuses (ROW-005: a manually-set list) ──────────────────────

public sealed record ListParcelStatusesQuery : IRequest<IReadOnlyList<ParcelStatusDto>>;

public sealed class ListParcelStatusesQueryHandler : IRequestHandler<ListParcelStatusesQuery, IReadOnlyList<ParcelStatusDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public ListParcelStatusesQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<ParcelStatusDto>> Handle(ListParcelStatusesQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        return (await _db.ParcelStatuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(ct)).Select(s => s.ToDto()).ToList();
    }
}

/// <summary>Adds (no id) or edits a status. Making a status the initial one clears the flag on the previous one.</summary>
public sealed record SaveParcelStatusCommand(Guid? Id, string Name, string Color, int SortOrder, bool IsActive, bool IsInitial) : IRequest<ParcelStatusDto>;

public sealed class SaveParcelStatusCommandValidator : AbstractValidator<SaveParcelStatusCommand>
{
    public SaveParcelStatusCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(60);
        RuleFor(x => x.Color).Must(c => ConfigRules.Color.IsMatch(c ?? "")).WithMessage("Choose a colour like #2563eb.");
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000);
    }
}

public sealed class SaveParcelStatusCommandHandler : IRequestHandler<SaveParcelStatusCommand, ParcelStatusDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public SaveParcelStatusCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<ParcelStatusDto> Handle(SaveParcelStatusCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);
        var name = request.Name.Trim();
        var lower = name.ToLower();
        if (await _db.ParcelStatuses.AnyAsync(s => s.Name.ToLower() == lower && s.Id != request.Id, ct))
            throw new ConflictException("status_exists", $"There is already a status named '{name}'.");
        if (request.IsInitial && !request.IsActive) throw new BusinessRuleException("The initial status must be active.");

        ParcelStatus status;
        if (request.Id is { } id)
        {
            status = await _db.ParcelStatuses.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Parcel status", id);
            if (status.IsInitial && !request.IsInitial)
                throw new BusinessRuleException("A new parcel needs an initial status. Make another status the initial one instead.");
        }
        else
        {
            status = new ParcelStatus();
            _db.ParcelStatuses.Add(status);
        }

        if (request.IsInitial)
            foreach (var other in await _db.ParcelStatuses.Where(s => s.IsInitial && s.Id != status.Id).ToListAsync(ct)) other.IsInitial = false;

        status.Name = name;
        status.Color = request.Color.ToLowerInvariant();
        status.SortOrder = request.SortOrder;
        status.IsActive = request.IsActive;
        status.IsInitial = request.IsInitial;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("status_exists", $"There is already a status named '{name}'.");
        }
        return status.ToDto();
    }
}

// ── Custom fields (ROW-011) ─────────────────────────────────────────────

public sealed record ListCustomFieldsQuery : IRequest<IReadOnlyList<CustomFieldDto>>;

public sealed class ListCustomFieldsQueryHandler : IRequestHandler<ListCustomFieldsQuery, IReadOnlyList<CustomFieldDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public ListCustomFieldsQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<CustomFieldDto>> Handle(ListCustomFieldsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        return (await _db.CustomFieldDefinitions.Where(d => d.EntityType == "Parcel").OrderBy(d => d.SortOrder).ThenBy(d => d.Label).ToListAsync(ct))
            .Select(d => d.ToDto()).ToList();
    }
}

/// <summary>Adds a field (with a key and type) or edits one (label, options, required, active, order; the key and type never change).</summary>
public sealed record SaveCustomFieldCommand(
    Guid? Id, string Key, string Label, CustomFieldType FieldType, IReadOnlyList<string>? Options, bool IsRequired, bool IsActive, int SortOrder)
    : IRequest<CustomFieldDto>;

public sealed class SaveCustomFieldCommandValidator : AbstractValidator<SaveCustomFieldCommand>
{
    public SaveCustomFieldCommandValidator()
    {
        RuleFor(x => x.Key).Must(k => ConfigRules.Key.IsMatch(k ?? "")).WithMessage("The key starts with a lower-case letter and uses lower-case letters, digits and _ (40 characters at most).");
        RuleFor(x => x.Label).NotEmpty().WithMessage("Label is required.").MaximumLength(100);
        RuleFor(x => x.FieldType).IsInEnum();
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 1000);
        RuleFor(x => x.Options).Must(o => o is null || o.Count <= 50).WithMessage("A list can have at most 50 options.");
        RuleForEach(x => x.Options).NotEmpty().WithMessage("An option cannot be blank.").MaximumLength(100);
        RuleFor(x => x.Options).Must(o => o is { Count: > 0 }).When(x => x.FieldType == CustomFieldType.List).WithMessage("A list field needs at least one option.");
        RuleFor(x => x.Options)
            .Must(o => o is null || o.Select(v => v.Trim().ToLowerInvariant()).Distinct().Count() == o.Count)
            .WithMessage("The options must all be different.");
    }
}

public sealed class SaveCustomFieldCommandHandler : IRequestHandler<SaveCustomFieldCommand, CustomFieldDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public SaveCustomFieldCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<CustomFieldDto> Handle(SaveCustomFieldCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);

        CustomFieldDefinition def;
        if (request.Id is { } id)
        {
            def = await _db.CustomFieldDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Custom field", id);
            if (def.Key != request.Key || def.FieldType != request.FieldType)
                throw new BusinessRuleException("A field's key and type cannot change once parcels may hold values for it. Add a new field instead.");
        }
        else
        {
            if (await _db.CustomFieldDefinitions.AnyAsync(d => d.EntityType == "Parcel" && d.Key == request.Key, ct))
                throw new ConflictException("custom_field_exists", $"There is already a field with the key '{request.Key}'.");
            def = new CustomFieldDefinition { EntityType = "Parcel", Key = request.Key, FieldType = request.FieldType };
            _db.CustomFieldDefinitions.Add(def);
        }

        def.Label = request.Label.Trim();
        def.Options = def.FieldType == CustomFieldType.List ? request.Options!.Select(o => o.Trim()).ToList() : new List<string>();
        def.IsRequired = request.IsRequired;
        def.IsActive = request.IsActive;
        def.SortOrder = request.SortOrder;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("custom_field_exists", $"There is already a field with the key '{request.Key}'.");
        }
        return def.ToDto();
    }
}

// ── Statutory deadline rules (ROW-073) ──────────────────────────────────

public sealed record ListDeadlineRulesQuery : IRequest<IReadOnlyList<DeadlineRuleDto>>;

public sealed class ListDeadlineRulesQueryHandler : IRequestHandler<ListDeadlineRulesQuery, IReadOnlyList<DeadlineRuleDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public ListDeadlineRulesQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<DeadlineRuleDto>> Handle(ListDeadlineRulesQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        return (await _db.DeadlineRules.OrderBy(r => r.Name).ToListAsync(ct)).Select(r => r.ToDto()).ToList();
    }
}

/// <summary>Adds or edits a deadline rule. Saving a seeded rule confirms it, so it is no longer shown as a placeholder.</summary>
public sealed record SaveDeadlineRuleCommand(Guid? Id, string Name, string TriggerMilestone, string TargetMilestone, int Days, bool IsActive)
    : IRequest<DeadlineRuleDto>;

public sealed class SaveDeadlineRuleCommandValidator : AbstractValidator<SaveDeadlineRuleCommand>
{
    public SaveDeadlineRuleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(150);
        RuleFor(x => x.TriggerMilestone).NotEmpty().WithMessage("Choose the milestone that starts the clock.").MaximumLength(100);
        RuleFor(x => x.TargetMilestone).NotEmpty().WithMessage("Choose the milestone that is due.").MaximumLength(100);
        RuleFor(x => x).Must(x => !string.Equals(x.TriggerMilestone?.Trim(), x.TargetMilestone?.Trim(), StringComparison.OrdinalIgnoreCase))
            .WithMessage("The start and the due milestone must be different.").OverridePropertyName("TargetMilestone");
        RuleFor(x => x.Days).InclusiveBetween(1, 3650).WithMessage("Days must be between 1 and 3650.");
    }
}

public sealed class SaveDeadlineRuleCommandHandler : IRequestHandler<SaveDeadlineRuleCommand, DeadlineRuleDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public SaveDeadlineRuleCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<DeadlineRuleDto> Handle(SaveDeadlineRuleCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);
        var name = request.Name.Trim();
        var lower = name.ToLower();
        if (await _db.DeadlineRules.AnyAsync(r => r.Name.ToLower() == lower && r.Id != request.Id, ct))
            throw new ConflictException("deadline_rule_exists", $"There is already a rule named '{name}'.");

        DeadlineRule rule;
        if (request.Id is { } id) rule = await _db.DeadlineRules.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Deadline rule", id);
        else
        {
            rule = new DeadlineRule();
            _db.DeadlineRules.Add(rule);
        }
        rule.Name = name;
        rule.TriggerMilestone = request.TriggerMilestone.Trim();
        rule.TargetMilestone = request.TargetMilestone.Trim();
        rule.Days = request.Days;
        rule.IsActive = request.IsActive;
        rule.IsPlaceholder = false; // an administrator has now looked at it
        await _db.SaveChangesAsync(ct);
        return rule.ToDto();
    }
}

// ── Workflows (ROW-071, read-only here) ─────────────────────────────────

public sealed record ListWorkflowsQuery : IRequest<IReadOnlyList<WorkflowDto>>;

public sealed class ListWorkflowsQueryHandler : IRequestHandler<ListWorkflowsQuery, IReadOnlyList<WorkflowDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public ListWorkflowsQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<WorkflowDto>> Handle(ListWorkflowsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        // Sorted here, not in SQL: the column holds the enum's name, so the database would order the types alphabetically.
        return (await _db.WorkflowTemplates.Include(t => t.Steps).ToListAsync(ct))
            .OrderBy(t => t.AcquisitionType)
            .Select(t => new WorkflowDto(t.AcquisitionType,
                t.Steps.OrderBy(s => s.Order).Select(s => new WorkflowStepDto(s.Key, s.Label, s.Order, s.RequiresAcquisitionGate)).ToList()))
            .ToList();
    }
}

// ── Who may override step order (ROW-072) ───────────────────────────────

public sealed record GetStepGateSettingsQuery : IRequest<StepGateSettingsDto>;

public sealed class GetStepGateSettingsQueryHandler : IRequestHandler<GetStepGateSettingsQuery, StepGateSettingsDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public GetStepGateSettingsQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<StepGateSettingsDto> Handle(GetStepGateSettingsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        return StepGateSettingsFactory.Build((await _db.StepGateSettings.FirstOrDefaultAsync(ct))?.ExtraOverrideRoles ?? new List<string>());
    }
}

internal static class StepGateSettingsFactory
{
    public static StepGateSettingsDto Build(IReadOnlyList<string> extra) =>
        new(StepGate.AlwaysMayOverride, extra, RoleNames.All.Where(r => !StepGate.AlwaysMayOverride.Contains(r)).ToList());
}

/// <summary>Replaces the extra roles that may override step order. Admin and ProjectManager always can and are not listed.</summary>
public sealed record SetStepGateSettingsCommand(IReadOnlyList<string> ExtraOverrideRoles) : IRequest<StepGateSettingsDto>;

public sealed class SetStepGateSettingsCommandValidator : AbstractValidator<SetStepGateSettingsCommand>
{
    public SetStepGateSettingsCommandValidator()
    {
        RuleFor(x => x.ExtraOverrideRoles).NotNull();
        RuleForEach(x => x.ExtraOverrideRoles).Must(RoleNames.IsKnown).WithMessage("Unknown role.")
            .Must(r => !StepGate.AlwaysMayOverride.Contains(r)).WithMessage("Administrators and project managers can always override.");
        RuleFor(x => x.ExtraOverrideRoles).Must(r => r is null || r.Distinct().Count() == r.Count).WithMessage("A role is listed twice.");
    }
}

public sealed class SetStepGateSettingsCommandHandler : IRequestHandler<SetStepGateSettingsCommand, StepGateSettingsDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public SetStepGateSettingsCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<StepGateSettingsDto> Handle(SetStepGateSettingsCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);
        var settings = await _db.StepGateSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new StepGateSettings();
            _db.StepGateSettings.Add(settings);
        }
        settings.ExtraOverrideRoles = request.ExtraOverrideRoles.ToList();
        await _db.SaveChangesAsync(ct);
        return StepGateSettingsFactory.Build(settings.ExtraOverrideRoles);
    }
}

// ── Next-action reminders (ROW-074): computed, never emailed ────────────

/// <summary>Deadlines that are overdue or due within <see cref="WithinDays"/> days across all live parcels.</summary>
public sealed record ListRemindersQuery(int WithinDays = StatutoryDeadlines.DefaultDueSoonDays) : IRequest<IReadOnlyList<ReminderDto>>;

public sealed class ListRemindersQueryValidator : AbstractValidator<ListRemindersQuery>
{
    public ListRemindersQueryValidator() => RuleFor(x => x.WithinDays).InclusiveBetween(0, 90);
}

public sealed class ListRemindersQueryHandler : IRequestHandler<ListRemindersQuery, IReadOnlyList<ReminderDto>>
{
    private const int MaxReminders = 200;

    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public ListRemindersQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<IReadOnlyList<ReminderDto>> Handle(ListRemindersQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        var rules = (await _db.DeadlineRules.Where(r => r.IsActive).ToListAsync(ct))
            .Select(r => new DeadlineRuleInfo(r.Id, r.Name, r.TriggerMilestone, r.TargetMilestone, r.Days)).ToList();
        if (rules.Count == 0) return Array.Empty<ReminderDto>();

        // Narrow to the parcels that can possibly need attention in the database, rule by rule: the trigger milestone is done
        // early enough that the target is overdue or due within the window, and the target is not done yet. Everything else
        // (most parcels) is never loaded, so no parcel is skipped however many there are.
        var today = _parcels.Today;
        var candidateIds = new HashSet<Guid>();
        foreach (var rule in rules)
        {
            var trigger = rule.TriggerMilestone.ToLowerInvariant();
            var target = rule.TargetMilestone.ToLowerInvariant();
            var startedBy = today.AddDays(request.WithinDays - rule.Days);
            candidateIds.UnionWith(await _db.Parcels
                .Where(p => p.State == ParcelState.Active
                    && p.Milestones.Any(m => m.Name.ToLower() == trigger && m.CompletedOn != null && m.CompletedOn <= startedBy)
                    && !p.Milestones.Any(m => m.Name.ToLower() == target && m.CompletedOn != null))
                .Select(p => p.Id)
                .ToListAsync(ct));
        }
        if (candidateIds.Count == 0) return Array.Empty<ReminderDto>();

        var ids = candidateIds.ToList();
        var parcels = await _db.Parcels.Include(p => p.Milestones).Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var projectIds = parcels.Select(p => p.ProjectId).Distinct().ToList();
        var projects = await _db.Projects.Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, ct);

        var all = parcels.SelectMany(p =>
        {
            var milestones = p.Milestones.Select(m => new MilestoneInfo(m.Name, m.DueOn, m.CompletedOn)).ToList();
            return StatutoryDeadlines.NeedingAttention(StatutoryDeadlines.Compute(rules, milestones, today, request.WithinDays))
                .Select(d => new ReminderDto(p.Id, p.Number, p.ProjectId, projects.GetValueOrDefault(p.ProjectId, "?"), d.Rule.Name, d.Rule.TargetMilestone,
                    d.DueOn!.Value, d.Status.ToString(), d.DaysRemaining!.Value));
        });

        return all.OrderBy(r => r.Status == nameof(DeadlineStatus.Overdue) ? 0 : 1).ThenBy(r => r.DueOn).ThenBy(r => r.ParcelNumber).Take(MaxReminders).ToList();
    }
}
