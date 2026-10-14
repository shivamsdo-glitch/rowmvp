using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Parcels;

internal static class ParcelRules
{
    public const decimal MaxArea = 10_000_000_000m;

    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>The editable fields of a parcel, shared by create and update.</summary>
public interface IParcelFields
{
    string? Number { get; }
    Guid? ParentTractId { get; }
    Guid? GroupId { get; }
    string? LegalDescription { get; }
    string? Address { get; }
    decimal AreaSqFt { get; }
    string? PropertyType { get; }
    Guid? StatusId { get; }
    AcquisitionType AcquisitionType { get; }
    IReadOnlyDictionary<string, string?>? CustomValues { get; }
}

public abstract class ParcelFieldsValidator<T> : AbstractValidator<T> where T : IParcelFields
{
    protected ParcelFieldsValidator()
    {
        RuleFor(x => x.Number).MaximumLength(50).Matches(@"^[A-Za-z0-9\-_/.]+$").When(x => !string.IsNullOrWhiteSpace(x.Number))
            .WithMessage("Parcel number may contain only letters, digits and - _ / .");
        RuleFor(x => x.LegalDescription).MaximumLength(4000);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.AreaSqFt).InclusiveBetween(0m, ParcelRules.MaxArea).WithMessage("Area must be zero or more square feet.");
        RuleFor(x => x.PropertyType).MaximumLength(100);
        RuleFor(x => x.AcquisitionType).IsInEnum();
    }
}

// ── Create (ROW-005) ────────────────────────────────────────────────────

/// <summary>Leave <see cref="Number"/> empty to take the next number in the project; <see cref="StatusId"/> empty starts in the initial status.</summary>
public sealed record CreateParcelCommand(
    Guid ProjectId, string? Number, Guid? ParentTractId, Guid? GroupId, string? LegalDescription, string? Address, decimal AreaSqFt,
    string? PropertyType, Guid? StatusId, AcquisitionType AcquisitionType, IReadOnlyDictionary<string, string?>? CustomValues)
    : IRequest<ParcelDto>, IParcelFields;

public sealed class CreateParcelCommandValidator : ParcelFieldsValidator<CreateParcelCommand>
{
    public CreateParcelCommandValidator() => RuleFor(x => x.ProjectId).NotEmpty();
}

public sealed class CreateParcelCommandHandler : IRequestHandler<CreateParcelCommand, ParcelDto>
{
    private const int MaxAttempts = 5;
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public CreateParcelCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelDto> Handle(CreateParcelCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Create, ct);
        if (!await _db.Projects.AnyAsync(p => p.Id == request.ProjectId, ct)) throw new NotFoundException("Project", request.ProjectId);

        var status = request.StatusId is { } sid ? await _parcels.SelectableStatusAsync(sid, ct) : await _parcels.InitialStatusAsync(ct);
        await _parcels.EnsureGroupAsync(request.GroupId, request.ProjectId, ct);
        await _parcels.EnsureTractAsync(request.ParentTractId, request.ProjectId, ct);
        var template = await _parcels.TemplateAsync(request.AcquisitionType, ct);
        var customJson = await _parcels.CustomValuesJsonAsync(request.CustomValues ?? new Dictionary<string, string?>(), "{}", ct);

        Parcel Build(string number)
        {
            var parcel = new Parcel
            {
                ProjectId = request.ProjectId,
                Number = number,
                ParentTractId = request.ParentTractId,
                GroupId = request.GroupId,
                LegalDescription = ParcelRules.Clean(request.LegalDescription),
                Address = ParcelRules.Clean(request.Address),
                AreaSqFt = request.AreaSqFt,
                PropertyType = ParcelRules.Clean(request.PropertyType),
                StatusId = status.Id,
                AcquisitionType = request.AcquisitionType,
                CurrentStepKey = template.Steps.OrderBy(s => s.Order).First().Key,
                CustomValuesJson = customJson,
            };
            _parcels.AddStandardMilestones(parcel);
            return parcel;
        }

        if (!string.IsNullOrWhiteSpace(request.Number))
        {
            var typed = request.Number.Trim();
            await _parcels.EnsureNumberFreeAsync(request.ProjectId, typed, null, ct);
            var saved = await TrySaveAsync(Build(typed), ct)
                        ?? throw new ConflictException("parcel_number_exists", $"This project already has a parcel numbered '{typed}'.");
            return await _parcels.ToDtoAsync(saved, ct);
        }

        // A generated number can lose a race with another create in the same project; nothing was saved, so choose again.
        var gate = ParcelService.NumberLock(request.ProjectId);
        await gate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var saved = await TrySaveAsync(Build(await _parcels.NextNumberAsync(request.ProjectId, ct)), ct);
                if (saved is not null) return await _parcels.ToDtoAsync(saved, ct);
            }
        }
        finally
        {
            gate.Release();
        }
        throw new ConflictException("parcel_number_conflict", "The system is busy issuing parcel numbers. Please try again.");
    }

    private async Task<Parcel?> TrySaveAsync(Parcel parcel, CancellationToken ct)
    {
        _db.Parcels.Add(parcel);
        try
        {
            await _db.SaveChangesAsync(ct);
            return parcel;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return null;
        }
    }
}

// ── Update ──────────────────────────────────────────────────────────────

public sealed record UpdateParcelCommand(
    Guid Id, string? Number, Guid? ParentTractId, Guid? GroupId, string? LegalDescription, string? Address, decimal AreaSqFt,
    string? PropertyType, Guid? StatusId, AcquisitionType AcquisitionType, IReadOnlyDictionary<string, string?>? CustomValues)
    : IRequest<ParcelDto>, IParcelFields;

public sealed class UpdateParcelCommandValidator : ParcelFieldsValidator<UpdateParcelCommand>
{
    public UpdateParcelCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class UpdateParcelCommandHandler : IRequestHandler<UpdateParcelCommand, ParcelDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public UpdateParcelCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelDto> Handle(UpdateParcelCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.Id, ct);

        var number = string.IsNullOrWhiteSpace(request.Number) ? parcel.Number : request.Number.Trim();
        if (!string.Equals(number, parcel.Number, StringComparison.OrdinalIgnoreCase))
            await _parcels.EnsureNumberFreeAsync(parcel.ProjectId, number, parcel.Id, ct);

        await _parcels.EnsureGroupAsync(request.GroupId, parcel.ProjectId, ct);
        await _parcels.EnsureTractAsync(request.ParentTractId, parcel.ProjectId, ct);

        // The status can be left as it is even when it has since been retired; choosing a different one needs an active status.
        var statusId = request.StatusId ?? parcel.StatusId;
        if (statusId != parcel.StatusId) await _parcels.SelectableStatusAsync(statusId, ct);

        // The workflow is chosen at the start; once the parcel has moved on, a different path would orphan its progress.
        if (request.AcquisitionType != parcel.AcquisitionType)
        {
            var current = await _parcels.TemplateAsync(parcel.AcquisitionType, ct);
            if (current.Steps.OrderBy(s => s.Order).First().Key != parcel.CurrentStepKey)
                throw new ConflictException("acquisition_type_locked", "The acquisition type can only be changed while the parcel is still on its first step.");
            var next = await _parcels.TemplateAsync(request.AcquisitionType, ct);
            parcel.CurrentStepKey = next.Steps.OrderBy(s => s.Order).First().Key;
            parcel.AcquisitionType = request.AcquisitionType;
        }

        parcel.Number = number;
        parcel.ParentTractId = request.ParentTractId;
        parcel.GroupId = request.GroupId;
        parcel.LegalDescription = ParcelRules.Clean(request.LegalDescription);
        parcel.Address = ParcelRules.Clean(request.Address);
        parcel.AreaSqFt = request.AreaSqFt;
        parcel.PropertyType = ParcelRules.Clean(request.PropertyType);
        parcel.StatusId = statusId;
        parcel.CustomValuesJson = await _parcels.CustomValuesJsonAsync(request.CustomValues, parcel.CustomValuesJson, ct);

        await _db.SaveChangesAsync(ct);
        return await _parcels.ToDtoAsync(parcel, ct);
    }
}

// ── Get / List ──────────────────────────────────────────────────────────

public sealed record GetParcelQuery(Guid Id) : IRequest<ParcelDto>;

public sealed class GetParcelQueryHandler : IRequestHandler<GetParcelQuery, ParcelDto>
{
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public GetParcelQueryHandler(IPagePermissionService permissions, ParcelService parcels)
    {
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelDto> Handle(GetParcelQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        return await _parcels.ToDtoAsync(await _parcels.LoadAsync(request.Id, ct), ct);
    }
}

/// <summary>Superseded parcels (split or combined away) are left out unless <see cref="IncludeSuperseded"/> is set.</summary>
public sealed record ListParcelsQuery(
    Guid? ProjectId, Guid? GroupId, Guid? StatusId, Guid? ParentTractId, string? Search, bool IncludeSuperseded = false, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<ParcelSummaryDto>>;

public sealed class ListParcelsQueryValidator : AbstractValidator<ListParcelsQuery>
{
    public ListParcelsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public sealed class ListParcelsQueryHandler : IRequestHandler<ListParcelsQuery, PagedResult<ParcelSummaryDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public ListParcelsQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<PagedResult<ParcelSummaryDto>> Handle(ListParcelsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);

        var query = _db.Parcels.AsQueryable();
        if (!request.IncludeSuperseded) query = query.Where(p => p.State == ParcelState.Active);
        if (request.ProjectId is { } project) query = query.Where(p => p.ProjectId == project);
        if (request.GroupId is { } group) query = query.Where(p => p.GroupId == group);
        if (request.StatusId is { } status) query = query.Where(p => p.StatusId == status);
        if (request.ParentTractId is { } tract) query = query.Where(p => p.ParentTractId == tract);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(p => p.Number.ToLower().Contains(term) || (p.Address != null && p.Address.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(p => p.ProjectId).ThenBy(p => p.Number)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new PagedResult<ParcelSummaryDto>(await _parcels.SummariesAsync(rows, ct), total, request.Page, request.PageSize);
    }
}

// ── Batch update (ROW-008) ──────────────────────────────────────────────

/// <summary>Sets the status and/or group of many parcels at once. All of them change or none do.</summary>
public sealed record BatchUpdateParcelsCommand(IReadOnlyList<Guid> ParcelIds, Guid? StatusId, Guid? GroupId, bool ClearGroup)
    : IRequest<BatchUpdateResultDto>;

public sealed class BatchUpdateParcelsCommandValidator : AbstractValidator<BatchUpdateParcelsCommand>
{
    public const int MaxParcels = 200;

    public BatchUpdateParcelsCommandValidator()
    {
        RuleFor(x => x.ParcelIds).NotEmpty().WithMessage("Choose at least one parcel.")
            .Must(ids => ids.Count <= MaxParcels).WithMessage($"Update at most {MaxParcels} parcels at a time.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("A parcel is listed twice.");
        RuleFor(x => x).Must(x => x.StatusId is not null || x.GroupId is not null || x.ClearGroup)
            .WithMessage("Choose a status or a group to apply.").OverridePropertyName("Changes");
        RuleFor(x => x).Must(x => !(x.GroupId is not null && x.ClearGroup))
            .WithMessage("Choose a group or clear the group, not both.").OverridePropertyName("Changes");
    }
}

public sealed class BatchUpdateParcelsCommandHandler : IRequestHandler<BatchUpdateParcelsCommand, BatchUpdateResultDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public BatchUpdateParcelsCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<BatchUpdateResultDto> Handle(BatchUpdateParcelsCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Update, ct);
        var ids = request.ParcelIds.ToList();
        var parcels = await _db.Parcels.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        if (parcels.Count != ids.Count) throw new NotFoundException("Parcel", string.Join(", ", ids.Except(parcels.Select(p => p.Id))));
        if (parcels.FirstOrDefault(p => p.State != ParcelState.Active) is { } gone)
            throw new ConflictException("parcel_not_active", $"Parcel {gone.Number} was {gone.State.ToString().ToLowerInvariant()} and can no longer be changed.");

        if (request.StatusId is { } statusId) await _parcels.SelectableStatusAsync(statusId, ct);
        if (request.GroupId is { } groupId)
        {
            var projects = parcels.Select(p => p.ProjectId).Distinct().ToList();
            if (projects.Count != 1) throw new InvalidRequestException("invalid_group", "A group belongs to one project; choose parcels from a single project to assign it.");
            await _parcels.EnsureGroupAsync(groupId, projects[0], ct);
        }

        foreach (var p in parcels)
        {
            if (request.StatusId is { } s) p.StatusId = s;
            if (request.GroupId is { } g) p.GroupId = g;
            else if (request.ClearGroup) p.GroupId = null;
        }
        await _db.SaveChangesAsync(ct);
        return new BatchUpdateResultDto(parcels.Count);
    }
}

// ── Split with remnant (ROW-007) ────────────────────────────────────────

public sealed record SplitPart(decimal AreaSqFt);

/// <summary>
/// Splits a parcel into the given parts. Any area the parts do not use becomes a remnant parcel. The original stays on record
/// as split; the new parcels inherit its project, tract, group, status, workflow position and custom values.
/// </summary>
public sealed record SplitParcelCommand(Guid ParcelId, IReadOnlyList<SplitPart> Parts) : IRequest<ParcelSplitResultDto>;

public sealed class SplitParcelCommandValidator : AbstractValidator<SplitParcelCommand>
{
    public SplitParcelCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.Parts).NotEmpty().WithMessage("At least one part is required.")
            .Must(p => p.Count <= ParcelSplit.MaxParts).WithMessage($"A parcel can be split into at most {ParcelSplit.MaxParts} parts.");
        RuleForEach(x => x.Parts).ChildRules(p => p.RuleFor(x => x.AreaSqFt).GreaterThan(0m).LessThanOrEqualTo(ParcelRules.MaxArea)
            .WithMessage("Every part needs an area greater than zero."));
    }
}

public sealed class SplitParcelCommandHandler : IRequestHandler<SplitParcelCommand, ParcelSplitResultDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public SplitParcelCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelSplitResultDto> Handle(SplitParcelCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Create, ct);
        var source = await _parcels.LoadActiveAsync(request.ParcelId, ct);

        var areas = request.Parts.Select(p => p.AreaSqFt).ToList();
        var problems = ParcelSplit.Validate(source.AreaSqFt, areas);
        if (problems.Count > 0) throw new BusinessRuleException(string.Join(" ", problems));

        var plan = ParcelSplit.Plan(source.Number, source.AreaSqFt, areas);
        foreach (var number in plan.Numbers.Concat(plan.RemnantNumber is null ? Array.Empty<string>() : new[] { plan.RemnantNumber }))
            await _parcels.EnsureNumberFreeAsync(source.ProjectId, number, null, ct);

        Parcel NewPiece(string number, decimal area, bool remnant)
        {
            var piece = new Parcel
            {
                ProjectId = source.ProjectId,
                Number = number,
                ParentTractId = source.ParentTractId,
                GroupId = source.GroupId,
                LegalDescription = source.LegalDescription,
                Address = source.Address,
                AreaSqFt = area,
                PropertyType = source.PropertyType,
                StatusId = source.StatusId,
                AcquisitionType = source.AcquisitionType,
                CurrentStepKey = source.CurrentStepKey,
                CustomValuesJson = source.CustomValuesJson,
                SourceParcelIds = new List<Guid> { source.Id },
                IsRemnant = remnant,
            };
            _parcels.AddStandardMilestones(piece);
            return piece;
        }

        var parts = plan.Numbers.Select((n, i) => NewPiece(n, plan.PartAreas[i], false)).ToList();
        var remnant = plan.RemnantNumber is null ? null : NewPiece(plan.RemnantNumber, plan.RemnantAreaSqFt, true);

        source.State = ParcelState.Split;
        source.SupersededOn = _parcels.Now;
        _db.Parcels.AddRange(parts);
        if (remnant is not null) _db.Parcels.Add(remnant);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("parcel_changed", "Someone else changed this parcel at the same moment. Please open it again and retry.");
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("parcel_number_exists", "Another parcel took one of the new numbers at the same moment. Please try again.");
        }

        var summaries = await _parcels.SummariesAsync(parts.Concat(remnant is null ? Array.Empty<Parcel>() : new[] { remnant }).ToList(), ct);
        return new ParcelSplitResultDto(
            await _parcels.ToDtoAsync(source, ct),
            summaries.Where(s => !s.IsRemnant).OrderBy(s => s.Number).ToList(),
            summaries.FirstOrDefault(s => s.IsRemnant));
    }
}

// ── Combine (ROW-007) ───────────────────────────────────────────────────

/// <summary>Combines parcels of one project into a new parcel with their total area. The originals stay on record as combined.</summary>
public sealed record CombineParcelsCommand(IReadOnlyList<Guid> ParcelIds, string? Number) : IRequest<ParcelDto>;

public sealed class CombineParcelsCommandValidator : AbstractValidator<CombineParcelsCommand>
{
    public CombineParcelsCommandValidator()
    {
        RuleFor(x => x.ParcelIds).NotEmpty().Must(ids => ids.Count >= 2).WithMessage("Choose at least two parcels to combine.")
            .Must(ids => ids.Count <= ParcelSplit.MaxParts).WithMessage($"Combine at most {ParcelSplit.MaxParts} parcels at a time.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("A parcel is listed twice.");
        RuleFor(x => x.Number).MaximumLength(50).Matches(@"^[A-Za-z0-9\-_/.]+$").When(x => !string.IsNullOrWhiteSpace(x.Number))
            .WithMessage("Parcel number may contain only letters, digits and - _ / .");
    }
}

public sealed class CombineParcelsCommandHandler : IRequestHandler<CombineParcelsCommand, ParcelDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public CombineParcelsCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelDto> Handle(CombineParcelsCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Create, ct);
        var ids = request.ParcelIds.ToList();
        var sources = await _db.Parcels.Include(p => p.Interests).Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        if (sources.Count != ids.Count) throw new NotFoundException("Parcel", string.Join(", ", ids.Except(sources.Select(p => p.Id))));
        if (sources.FirstOrDefault(p => p.State != ParcelState.Active) is { } gone)
            throw new ConflictException("parcel_not_active", $"Parcel {gone.Number} was {gone.State.ToString().ToLowerInvariant()} and can no longer be combined.");
        if (sources.Select(p => p.ProjectId).Distinct().Count() != 1)
            throw new InvalidRequestException("invalid_combination", "Parcels from different projects cannot be combined.");
        if (sources.Select(p => p.AcquisitionType).Distinct().Count() != 1)
            throw new InvalidRequestException("invalid_combination", "Parcels with different acquisition types cannot be combined.");

        var projectId = sources[0].ProjectId;
        var template = await _parcels.TemplateAsync(sources[0].AcquisitionType, ct);
        var order = template.Steps.ToDictionary(s => s.Key, s => s.Order);
        // The combined parcel is only as far along as its least advanced source.
        var earliest = sources.OrderBy(p => order.GetValueOrDefault(p.CurrentStepKey, int.MaxValue)).First();

        bool Same<T>(Func<Parcel, T> f) => sources.Select(f).Distinct().Count() == 1;
        var initial = await _parcels.InitialStatusAsync(ct);

        Parcel Build(string number)
        {
            var combined = new Parcel
            {
                ProjectId = projectId,
                Number = number,
                ParentTractId = Same(p => p.ParentTractId) ? sources[0].ParentTractId : null,
                GroupId = Same(p => p.GroupId) ? sources[0].GroupId : null,
                LegalDescription = string.Join("\n\n", sources.Select(p => p.LegalDescription).Where(d => !string.IsNullOrWhiteSpace(d))) is { Length: > 0 } d && d.Length <= 4000 ? d : sources[0].LegalDescription,
                Address = Same(p => p.Address) ? sources[0].Address : null,
                AreaSqFt = sources.Sum(p => p.AreaSqFt),
                PropertyType = Same(p => p.PropertyType) ? sources[0].PropertyType : null,
                StatusId = Same(p => p.StatusId) ? sources[0].StatusId : initial.Id,
                AcquisitionType = sources[0].AcquisitionType,
                CurrentStepKey = earliest.CurrentStepKey,
                CustomValuesJson = sources[0].CustomValuesJson,
                SourceParcelIds = ids,
            };
            _parcels.AddStandardMilestones(combined);
            foreach (var group in sources.SelectMany(p => p.Interests).GroupBy(i => i.Type))
                combined.Interests.Add(new ParcelInterest { ParcelId = combined.Id, Type = group.Key, AreaSqFt = group.Sum(i => i.AreaSqFt) });
            return combined;
        }

        var gate = ParcelService.NumberLock(projectId);
        await gate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                string number;
                if (!string.IsNullOrWhiteSpace(request.Number))
                {
                    number = request.Number.Trim();
                    await _parcels.EnsureNumberFreeAsync(projectId, number, null, ct);
                }
                else number = await _parcels.NextNumberAsync(projectId, ct);

                var combined = Build(number);
                _db.Parcels.Add(combined);
                foreach (var s in sources)
                {
                    s.State = ParcelState.Combined;
                    s.SupersededOn = _parcels.Now;
                }
                try
                {
                    await _db.SaveChangesAsync(ct);
                    return await _parcels.ToDtoAsync(combined, ct);
                }
                catch (DbUpdateException)
                {
                    _db.ChangeTracker.Clear();
                    if (!string.IsNullOrWhiteSpace(request.Number))
                        throw new ConflictException("parcel_number_exists", $"This project already has a parcel numbered '{request.Number.Trim()}'.");
                    // The sources were detached by Clear(); reload them for the next attempt.
                    sources = await _db.Parcels.Include(p => p.Interests).Where(p => ids.Contains(p.Id)).ToListAsync(ct);
                    if (sources.Any(p => p.State != ParcelState.Active))
                        throw new ConflictException("parcel_not_active", "One of the parcels was changed by someone else. Please try again.");
                }
            }
        }
        finally
        {
            gate.Release();
        }
        throw new ConflictException("parcel_number_conflict", "The system is busy issuing parcel numbers. Please try again.");
    }
}
