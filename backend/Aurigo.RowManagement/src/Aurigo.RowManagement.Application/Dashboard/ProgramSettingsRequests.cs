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

namespace Aurigo.RowManagement.Application.Dashboard;

// ── The thresholds and the step-to-team mapping behind the Program view (ADR 0025) ──────────────────────────────
// Read and changed by whoever may read and change the Configuration page. A tenant that never saves anything uses ProgramRules.Default
// and StageTimes.DefaultTeamOf, so nothing needs to be provisioned.

public sealed record ProgramRulesDto(int StuckDays, int ClearDateWarningDays, int ClearDateWarningPercentClosed, int RedOverdueMinimumParcels, int RedOverduePercent);
public sealed record StepTeamDto(string StepKey, string Label, string Team, string DefaultTeam);
public sealed record ProgramSettingsDto(ProgramRulesDto Rules, ProgramRulesDto Defaults, IReadOnlyList<StepTeamDto> Steps, IReadOnlyList<string> Teams);
public sealed record StepTeamInput(string StepKey, string Team);

internal static class ProgramConfig
{
    public static ProgramRulesDto ToDto(ProgramRules r) => new(r.StuckDays, r.ClearDateWarningDays, r.ClearDateWarningPercentClosed, r.RedOverdueMinimumParcels, r.RedOverduePercent);

    public static ProgramRules ToRules(ProgramSettings s) => new(s.StuckDays, s.ClearDateWarningDays, s.ClearDateWarningPercentClosed, s.RedOverdueMinimumParcels, s.RedOverduePercent);

    /// <summary>The agency's thresholds (the defaults when it has never saved any) and the steps it has assigned to a different team.</summary>
    public static async Task<(ProgramRules Rules, IReadOnlyDictionary<string, string> Teams)> LoadAsync(IRowDbContext db, CancellationToken ct)
    {
        var settings = await db.ProgramSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var teams = await db.ProgramStepTeams.AsNoTracking().ToDictionaryAsync(t => t.StepKey, t => t.Team, StringComparer.Ordinal, ct);
        return (settings is null ? ProgramRules.Default : ToRules(settings), teams);
    }

    /// <summary>Every step of every workflow once: the standard steps in order, then the steps only another acquisition type has.</summary>
    public static async Task<List<(string Key, string Label)>> StepsAsync(IRowDbContext db, CancellationToken ct)
    {
        var templates = await db.WorkflowTemplates.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.AcquisitionType, ct);
        var steps = (await db.WorkflowSteps.AsNoTracking().Select(s => new { s.TemplateId, s.Key, s.Label, s.Order }).ToListAsync(ct))
            .Select(s => new { s.Key, s.Label, s.Order, Standard = templates.GetValueOrDefault(s.TemplateId) == AcquisitionType.Standard }).ToList();
        var standard = steps.Where(s => s.Standard).OrderBy(s => s.Order).Select(s => s.Key).Distinct().ToList();
        var extra = steps.Where(s => !standard.Contains(s.Key)).GroupBy(s => s.Key).OrderBy(g => g.Min(x => x.Order)).Select(g => g.Key);
        return standard.Concat(extra).Select(k => (k, steps.First(s => s.Key == k).Label)).ToList();
    }

    public static async Task<ProgramSettingsDto> BuildAsync(IRowDbContext db, CancellationToken ct)
    {
        var (rules, chosen) = await LoadAsync(db, ct);
        var steps = (await StepsAsync(db, ct)).Select(s => new StepTeamDto(s.Key, s.Label, StageTimes.TeamOf(s.Key, chosen), StageTimes.DefaultTeamOf(s.Key))).ToList();
        var teams = steps.Select(s => s.Team).Concat(steps.Select(s => s.DefaultTeam)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        return new ProgramSettingsDto(ToDto(rules), ToDto(ProgramRules.Default), steps, teams);
    }
}

public sealed record GetProgramSettingsQuery : IRequest<ProgramSettingsDto>;

public sealed class GetProgramSettingsQueryHandler : IRequestHandler<GetProgramSettingsQuery, ProgramSettingsDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public GetProgramSettingsQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<ProgramSettingsDto> Handle(GetProgramSettingsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Read, ct);
        return await ProgramConfig.BuildAsync(_db, ct);
    }
}

/// <summary>Replaces the thresholds and the step-to-team mapping. A step left out, or given its default team, follows the default.</summary>
public sealed record SetProgramSettingsCommand(
    int StuckDays, int ClearDateWarningDays, int ClearDateWarningPercentClosed, int RedOverdueMinimumParcels, int RedOverduePercent,
    IReadOnlyList<StepTeamInput> Steps) : IRequest<ProgramSettingsDto>;

public sealed class SetProgramSettingsCommandValidator : AbstractValidator<SetProgramSettingsCommand>
{
    public const int MaxTeamLength = 50;

    public SetProgramSettingsCommandValidator()
    {
        RuleFor(x => x.StuckDays).InclusiveBetween(1, 365).WithMessage("Days on one step before a parcel is stuck must be between 1 and 365.");
        RuleFor(x => x.ClearDateWarningDays).InclusiveBetween(1, 365).WithMessage("The ROW clear date warning must be between 1 and 365 days.");
        RuleFor(x => x.ClearDateWarningPercentClosed).InclusiveBetween(1, 100).WithMessage("The percent closed must be between 1 and 100.");
        RuleFor(x => x.RedOverdueMinimumParcels).InclusiveBetween(1, 1000).WithMessage("The number of overdue parcels must be between 1 and 1000.");
        RuleFor(x => x.RedOverduePercent).InclusiveBetween(1, 100).WithMessage("The share of overdue parcels must be between 1 and 100 percent.");
        RuleFor(x => x.Steps).NotNull();
        RuleForEach(x => x.Steps).ChildRules(step =>
        {
            step.RuleFor(s => s.StepKey).NotEmpty().MaximumLength(64);
            step.RuleFor(s => s.Team).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Every step needs a team name.")
                .Must(t => t is null || t.Trim().Length <= MaxTeamLength).WithMessage($"A team name can be at most {MaxTeamLength} characters.");
        });
        RuleFor(x => x.Steps).Must(s => s is null || s.Select(i => i.StepKey).Distinct(StringComparer.Ordinal).Count() == s.Count).WithMessage("A step is listed twice.");
    }
}

public sealed class SetProgramSettingsCommandHandler : IRequestHandler<SetProgramSettingsCommand, ProgramSettingsDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public SetProgramSettingsCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<ProgramSettingsDto> Handle(SetProgramSettingsCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);
        var known = (await ProgramConfig.StepsAsync(_db, ct)).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var unknown = request.Steps.Select(s => s.StepKey).FirstOrDefault(k => !known.Contains(k));
        if (unknown is not null) throw new InvalidRequestException("unknown_step", $"'{unknown}' is not a step of any workflow.");

        var settings = await _db.ProgramSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new ProgramSettings();
            _db.ProgramSettings.Add(settings);
        }
        settings.StuckDays = request.StuckDays;
        settings.ClearDateWarningDays = request.ClearDateWarningDays;
        settings.ClearDateWarningPercentClosed = request.ClearDateWarningPercentClosed;
        settings.RedOverdueMinimumParcels = request.RedOverdueMinimumParcels;
        settings.RedOverduePercent = request.RedOverduePercent;

        // Keep only the steps that differ from the default, so a later change to a default still reaches the steps nobody chose.
        var wanted = request.Steps.Select(s => (s.StepKey, Team: s.Team.Trim())).Where(s => !string.Equals(s.Team, StageTimes.DefaultTeamOf(s.StepKey), StringComparison.Ordinal)).ToDictionary(s => s.StepKey, s => s.Team, StringComparer.Ordinal);
        var existing = await _db.ProgramStepTeams.ToListAsync(ct);
        foreach (var row in existing)
        {
            if (wanted.TryGetValue(row.StepKey, out var team)) { row.Team = team; wanted.Remove(row.StepKey); }
            else _db.ProgramStepTeams.Remove(row);
        }
        foreach (var (key, team) in wanted) _db.ProgramStepTeams.Add(new ProgramStepTeam { StepKey = key, Team = team });

        await _db.SaveChangesAsync(ct);
        return await ProgramConfig.BuildAsync(_db, ct);
    }
}

/// <summary>Puts the thresholds and the mapping back to the defaults.</summary>
public sealed record ResetProgramSettingsCommand : IRequest<ProgramSettingsDto>;

public sealed class ResetProgramSettingsCommandHandler : IRequestHandler<ResetProgramSettingsCommand, ProgramSettingsDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public ResetProgramSettingsCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<ProgramSettingsDto> Handle(ResetProgramSettingsCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);
        _db.ProgramSettings.RemoveRange(await _db.ProgramSettings.ToListAsync(ct));
        _db.ProgramStepTeams.RemoveRange(await _db.ProgramStepTeams.ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
        return await ProgramConfig.BuildAsync(_db, ct);
    }
}
