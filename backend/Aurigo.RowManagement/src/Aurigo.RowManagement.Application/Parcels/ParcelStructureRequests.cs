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

internal static class StructureRules
{
    public static async Task EnsureProjectAsync(IRowDbContext db, Guid projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct)) throw new NotFoundException("Project", projectId);
    }

    public static List<string> CleanNumbers(IEnumerable<string>? numbers) =>
        (numbers ?? Array.Empty<string>()).Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

// ── Parcel groups: the second WBS level (ROW-002, 006) ──────────────────

public sealed record ListParcelGroupsQuery(Guid ProjectId) : IRequest<IReadOnlyList<ParcelGroupDto>>;

public sealed class ListParcelGroupsQueryHandler : IRequestHandler<ListParcelGroupsQuery, IReadOnlyList<ParcelGroupDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public ListParcelGroupsQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<ParcelGroupDto>> Handle(ListParcelGroupsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        await StructureRules.EnsureProjectAsync(_db, request.ProjectId, ct);
        var groups = await _db.ParcelGroups.Where(g => g.ProjectId == request.ProjectId).OrderBy(g => g.Name).ToListAsync(ct);
        var counts = await _db.Parcels.Where(p => p.ProjectId == request.ProjectId && p.State == ParcelState.Active && p.GroupId != null)
            .GroupBy(p => p.GroupId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return groups.Select(g => new ParcelGroupDto(g.Id, g.ProjectId, g.Name, g.Description, counts.GetValueOrDefault(g.Id))).ToList();
    }
}

public sealed record SaveParcelGroupCommand(Guid ProjectId, Guid? Id, string Name, string? Description) : IRequest<ParcelGroupDto>;

public sealed class SaveParcelGroupCommandValidator : AbstractValidator<SaveParcelGroupCommand>
{
    public SaveParcelGroupCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class SaveParcelGroupCommandHandler : IRequestHandler<SaveParcelGroupCommand, ParcelGroupDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public SaveParcelGroupCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<ParcelGroupDto> Handle(SaveParcelGroupCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, request.Id is null ? PermissionAction.Create : PermissionAction.Update, ct);
        await StructureRules.EnsureProjectAsync(_db, request.ProjectId, ct);

        var name = request.Name.Trim();
        var lower = name.ToLower();
        if (await _db.ParcelGroups.AnyAsync(g => g.ProjectId == request.ProjectId && g.Name.ToLower() == lower && g.Id != request.Id, ct))
            throw new ConflictException("group_exists", $"This project already has a group named '{name}'.");

        ParcelGroup group;
        if (request.Id is { } id)
        {
            group = await _db.ParcelGroups.FirstOrDefaultAsync(g => g.Id == id && g.ProjectId == request.ProjectId, ct)
                    ?? throw new NotFoundException("Parcel group", id);
        }
        else
        {
            group = new ParcelGroup { ProjectId = request.ProjectId };
            _db.ParcelGroups.Add(group);
        }
        group.Name = name;
        group.Description = ParcelRules.Clean(request.Description);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("group_exists", $"This project already has a group named '{name}'.");
        }
        var count = await _db.Parcels.CountAsync(p => p.GroupId == group.Id && p.State == ParcelState.Active, ct);
        return new ParcelGroupDto(group.Id, group.ProjectId, group.Name, group.Description, count);
    }
}

/// <summary>Only an empty group can be removed; its parcels would otherwise lose their place in the project.</summary>
public sealed record DeleteParcelGroupCommand(Guid ProjectId, Guid Id) : IRequest;

public sealed class DeleteParcelGroupCommandHandler : IRequestHandler<DeleteParcelGroupCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public DeleteParcelGroupCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task Handle(DeleteParcelGroupCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Delete, ct);
        var group = await _db.ParcelGroups.FirstOrDefaultAsync(g => g.Id == request.Id && g.ProjectId == request.ProjectId, ct)
                    ?? throw new NotFoundException("Parcel group", request.Id);
        // Count every parcel, including split or combined ones: they still point at the group.
        if (await _db.Parcels.AnyAsync(p => p.GroupId == group.Id, ct))
            throw new ConflictException("group_not_empty", $"The group '{group.Name}' still has parcels. Move them to another group first.");
        _db.ParcelGroups.Remove(group);
        await _db.SaveChangesAsync(ct);
    }
}

// ── Parent tracts (ROW-233) ─────────────────────────────────────────────

public sealed record ListParentTractsQuery(Guid ProjectId) : IRequest<IReadOnlyList<ParentTractDto>>;

public sealed class ListParentTractsQueryHandler : IRequestHandler<ListParentTractsQuery, IReadOnlyList<ParentTractDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public ListParentTractsQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<ParentTractDto>> Handle(ListParentTractsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        await StructureRules.EnsureProjectAsync(_db, request.ProjectId, ct);
        var tracts = await _db.ParentTracts.Where(t => t.ProjectId == request.ProjectId).OrderBy(t => t.Number).ToListAsync(ct);
        var counts = await _db.Parcels.Where(p => p.ProjectId == request.ProjectId && p.State == ParcelState.Active && p.ParentTractId != null)
            .GroupBy(p => p.ParentTractId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return tracts.Select(t => new ParentTractDto(t.Id, t.ProjectId, t.Number, t.Description, t.TaxParcelNumbers, counts.GetValueOrDefault(t.Id))).ToList();
    }
}

public sealed record SaveParentTractCommand(Guid ProjectId, Guid? Id, string Number, string? Description, IReadOnlyList<string>? TaxParcelNumbers)
    : IRequest<ParentTractDto>;

public sealed class SaveParentTractCommandValidator : AbstractValidator<SaveParentTractCommand>
{
    public SaveParentTractCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Number).NotEmpty().WithMessage("Tract number is required.").MaximumLength(50)
            .Matches(@"^[A-Za-z0-9\-_/.]+$").WithMessage("Tract number may contain only letters, digits and - _ / .");
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.TaxParcelNumbers).Must(n => n is null || n.Count <= 50).WithMessage("At most 50 tax parcel numbers per tract.");
        RuleForEach(x => x.TaxParcelNumbers).MaximumLength(50).WithMessage("A tax parcel number is at most 50 characters.");
    }
}

public sealed class SaveParentTractCommandHandler : IRequestHandler<SaveParentTractCommand, ParentTractDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public SaveParentTractCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<ParentTractDto> Handle(SaveParentTractCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, request.Id is null ? PermissionAction.Create : PermissionAction.Update, ct);
        await StructureRules.EnsureProjectAsync(_db, request.ProjectId, ct);

        var number = request.Number.Trim();
        var lower = number.ToLower();
        if (await _db.ParentTracts.AnyAsync(t => t.ProjectId == request.ProjectId && t.Number.ToLower() == lower && t.Id != request.Id, ct))
            throw new ConflictException("tract_exists", $"This project already has a tract numbered '{number}'.");

        ParentTract tract;
        if (request.Id is { } id)
        {
            tract = await _db.ParentTracts.FirstOrDefaultAsync(t => t.Id == id && t.ProjectId == request.ProjectId, ct)
                    ?? throw new NotFoundException("Parent tract", id);
        }
        else
        {
            tract = new ParentTract { ProjectId = request.ProjectId };
            _db.ParentTracts.Add(tract);
        }
        tract.Number = number;
        tract.Description = ParcelRules.Clean(request.Description);
        tract.TaxParcelNumbers = StructureRules.CleanNumbers(request.TaxParcelNumbers);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("tract_exists", $"This project already has a tract numbered '{number}'.");
        }
        var count = await _db.Parcels.CountAsync(p => p.ParentTractId == tract.Id && p.State == ParcelState.Active, ct);
        return new ParentTractDto(tract.Id, tract.ProjectId, tract.Number, tract.Description, tract.TaxParcelNumbers, count);
    }
}

// ── Roll-up (ROW-006) ───────────────────────────────────────────────────

/// <summary>Parcel counts and areas for a project, by status and by group; optionally for one group (a corridor) only.</summary>
public sealed record GetParcelRollupQuery(Guid ProjectId, Guid? GroupId) : IRequest<ParcelRollupDto>;

public sealed class GetParcelRollupQueryHandler : IRequestHandler<GetParcelRollupQuery, ParcelRollupDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public GetParcelRollupQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<ParcelRollupDto> Handle(GetParcelRollupQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        await StructureRules.EnsureProjectAsync(_db, request.ProjectId, ct);

        var statuses = await _db.ParcelStatuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(ct);
        var groups = await _db.ParcelGroups.Where(g => g.ProjectId == request.ProjectId).OrderBy(g => g.Name).ToListAsync(ct);
        var query = _db.Parcels.Where(p => p.ProjectId == request.ProjectId && p.State == ParcelState.Active);
        if (request.GroupId is { } gid) query = query.Where(p => p.GroupId == gid);
        var parcels = await query.Select(p => new RollupParcel(p.StatusId, p.GroupId, p.AreaSqFt, p.State)).ToListAsync(ct);

        // Inactive statuses only show when parcels still use them.
        var statusIds = statuses.Where(s => s.IsActive || parcels.Any(p => p.StatusId == s.Id)).Select(s => s.Id).ToList();
        var result = ParcelRollup.Summarize(parcels, statusIds, groups.Select(g => g.Id).ToList());

        var statusById = statuses.ToDictionary(s => s.Id);
        var groupById = groups.ToDictionary(g => g.Id);
        return new ParcelRollupDto(
            result.TotalCount, result.TotalAreaSqFt, ParcelRollup.ToAcres(result.TotalAreaSqFt),
            result.ByStatus.Select(b => new RollupBucketDto(b.Key, statusById.TryGetValue(b.Key!.Value, out var s) ? s.Name : "Unknown", s?.Color, b.Count, b.AreaSqFt, ParcelRollup.ToAcres(b.AreaSqFt))).ToList(),
            result.ByGroup.Select(b => new RollupBucketDto(b.Key, b.Key is { } g && groupById.TryGetValue(g, out var grp) ? grp.Name : "No group", null, b.Count, b.AreaSqFt, ParcelRollup.ToAcres(b.AreaSqFt))).ToList());
    }
}
