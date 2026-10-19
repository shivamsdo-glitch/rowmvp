using System.Text.RegularExpressions;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Library;

// ── DTOs ────────────────────────────────────────────────────────────────

/// <summary><see cref="IsLibraryDriven"/>: people choose from a list defined here (a dropdown) rather than typing; true for List fields.</summary>
public sealed record LibraryFieldSummaryDto(Guid Id, string Key, string Label, CustomFieldType FieldType, bool IsSystem, int ValueCount, int ActiveCount, bool IsLibraryDriven);

/// <summary><see cref="IsDefault"/> marks the value new records start with, where the field has one (the initial parcel status).</summary>
public sealed record LibraryValueDto(Guid Id, string Value, bool IsActive, int SortOrder, bool IsDefault);

public sealed record LibraryFieldDto(Guid Id, string Key, string Label, CustomFieldType FieldType, bool IsSystem, IReadOnlyList<LibraryValueDto> Values, bool IsLibraryDriven);

public sealed record LibraryImportResultDto(int Added, int AlreadyThere, LibraryFieldDto Field);

// ── Shared rules ────────────────────────────────────────────────────────

internal static class LibraryRules
{
    public const int MaxImport = 500;
    public static readonly Regex Slug = new("[^a-z0-9]+", RegexOptions.Compiled);

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    public static async Task<LibraryField> LoadAsync(IRowDbContext db, Guid id, CancellationToken ct) =>
        await db.LibraryFields.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw new NotFoundException("Library field", id);

    public static bool IsParcelStatus(LibraryField f) => f.Key == LibraryKeys.ParcelStatus;

    /// <summary>The field with its values. The parcel status list is the agency's real status list, so it is read from there.</summary>
    public static async Task<LibraryFieldDto> ToDtoAsync(IRowDbContext db, LibraryField f, CancellationToken ct)
    {
        IReadOnlyList<LibraryValueDto> values;
        if (IsParcelStatus(f))
        {
            values = (await db.ParcelStatuses.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(ct))
                .Select(s => new LibraryValueDto(s.Id, s.Name, s.IsActive, s.SortOrder, s.IsInitial)).ToList();
        }
        else
        {
            values = (await db.LibraryValues.Where(v => v.LibraryFieldId == f.Id).OrderBy(v => v.SortOrder).ThenBy(v => v.Value).ToListAsync(ct))
                .Select(v => new LibraryValueDto(v.Id, v.Value, v.IsActive, v.SortOrder, false)).ToList();
        }
        return new LibraryFieldDto(f.Id, f.Key, f.Label, f.FieldType, f.IsSystem, values, f.FieldType == CustomFieldType.List);
    }

    public static void EnsureList(LibraryField f)
    {
        if (f.FieldType != CustomFieldType.List)
            throw new InvalidRequestException("not_a_list", $"'{f.Label}' is not a list, so it has no values to choose from.");
    }
}

// ── Reading ─────────────────────────────────────────────────────────────

public sealed record ListLibraryFieldsQuery : IRequest<IReadOnlyList<LibraryFieldSummaryDto>>;

public sealed class ListLibraryFieldsQueryHandler : IRequestHandler<ListLibraryFieldsQuery, IReadOnlyList<LibraryFieldSummaryDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public ListLibraryFieldsQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<LibraryFieldSummaryDto>> Handle(ListLibraryFieldsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Library, PermissionAction.Read, ct);
        var fields = await _db.LibraryFields.OrderBy(f => f.SortOrder).ThenBy(f => f.Label).ToListAsync(ct);
        var counts = (await _db.LibraryValues.GroupBy(v => v.LibraryFieldId)
            .Select(g => new { g.Key, Total = g.Count(), Active = g.Count(v => v.IsActive) }).ToListAsync(ct)).ToDictionary(x => x.Key);
        var statusTotal = await _db.ParcelStatuses.CountAsync(ct);
        var statusActive = await _db.ParcelStatuses.CountAsync(s => s.IsActive, ct);

        return fields.Select(f =>
        {
            if (LibraryRules.IsParcelStatus(f)) return new LibraryFieldSummaryDto(f.Id, f.Key, f.Label, f.FieldType, f.IsSystem, statusTotal, statusActive, true);
            counts.TryGetValue(f.Id, out var c);
            return new LibraryFieldSummaryDto(f.Id, f.Key, f.Label, f.FieldType, f.IsSystem, c?.Total ?? 0, c?.Active ?? 0, f.FieldType == CustomFieldType.List);
        }).ToList();
    }
}

public sealed record GetLibraryFieldQuery(Guid Id) : IRequest<LibraryFieldDto>;

public sealed class GetLibraryFieldQueryHandler : IRequestHandler<GetLibraryFieldQuery, LibraryFieldDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public GetLibraryFieldQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<LibraryFieldDto> Handle(GetLibraryFieldQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Library, PermissionAction.Read, ct);
        return await LibraryRules.ToDtoAsync(_db, await LibraryRules.LoadAsync(_db, request.Id, ct), ct);
    }
}

// ── Fields ──────────────────────────────────────────────────────────────

/// <summary>Defines a new field: its name and type. A list starts empty; its values are added afterwards.</summary>
public sealed record CreateLibraryFieldCommand(string Label, CustomFieldType FieldType) : IRequest<LibraryFieldDto>;

public sealed class CreateLibraryFieldCommandValidator : AbstractValidator<CreateLibraryFieldCommand>
{
    public CreateLibraryFieldCommandValidator()
    {
        RuleFor(x => x.Label).NotEmpty().WithMessage("Name is required.").MaximumLength(100);
        RuleFor(x => x.FieldType).IsInEnum();
    }
}

public sealed class CreateLibraryFieldCommandHandler : IRequestHandler<CreateLibraryFieldCommand, LibraryFieldDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public CreateLibraryFieldCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<LibraryFieldDto> Handle(CreateLibraryFieldCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Library, PermissionAction.Create, ct);
        var label = request.Label.Trim();
        var lower = label.ToLower();
        if (await _db.LibraryFields.AnyAsync(f => f.Label.ToLower() == lower, ct))
            throw new ConflictException("library_field_exists", $"There is already a field named '{label}'.");

        var slug = LibraryRules.Slug.Replace(label.ToLowerInvariant(), "_").Trim('_');
        if (slug.Length == 0 || !char.IsAsciiLetter(slug[0])) slug = "f_" + slug;
        slug = slug.Length > 36 ? slug[..36].TrimEnd('_') : slug;
        var key = slug;
        for (var n = 2; await _db.LibraryFields.AnyAsync(f => f.Key == key, ct); n++) key = $"{slug}_{n}";

        var field = new LibraryField
        {
            Key = key, Label = label, FieldType = request.FieldType, IsSystem = false,
            SortOrder = (await _db.LibraryFields.MaxAsync(f => (int?)f.SortOrder, ct) ?? 0) + 1,
        };
        _db.LibraryFields.Add(field);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("library_field_exists", $"There is already a field named '{label}'.");
        }
        return await LibraryRules.ToDtoAsync(_db, field, ct);
    }
}

/// <summary>Renames a field or changes its type. A system field keeps its type, and a list that has values stays a list.</summary>
public sealed record UpdateLibraryFieldCommand(Guid Id, string Label, CustomFieldType FieldType) : IRequest<LibraryFieldDto>;

public sealed class UpdateLibraryFieldCommandValidator : AbstractValidator<UpdateLibraryFieldCommand>
{
    public UpdateLibraryFieldCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Label).NotEmpty().WithMessage("Name is required.").MaximumLength(100);
        RuleFor(x => x.FieldType).IsInEnum();
    }
}

public sealed class UpdateLibraryFieldCommandHandler : IRequestHandler<UpdateLibraryFieldCommand, LibraryFieldDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public UpdateLibraryFieldCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<LibraryFieldDto> Handle(UpdateLibraryFieldCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Library, PermissionAction.Update, ct);
        var field = await LibraryRules.LoadAsync(_db, request.Id, ct);
        var label = request.Label.Trim();
        var lower = label.ToLower();
        if (await _db.LibraryFields.AnyAsync(f => f.Label.ToLower() == lower && f.Id != field.Id, ct))
            throw new ConflictException("library_field_exists", $"There is already a field named '{label}'.");

        if (request.FieldType != field.FieldType)
        {
            if (field.IsSystem) throw new BusinessRuleException($"The type of '{field.Label}' is fixed because the product uses it.");
            if (field.FieldType == CustomFieldType.List && await _db.LibraryValues.AnyAsync(v => v.LibraryFieldId == field.Id, ct))
                throw new ConflictException("library_field_has_values", $"'{field.Label}' already has values. Switch them off instead of changing the type.");
            field.FieldType = request.FieldType;
        }
        field.Label = label;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("library_field_exists", $"There is already a field named '{label}'.");
        }
        return await LibraryRules.ToDtoAsync(_db, field, ct);
    }
}

// ── Values ──────────────────────────────────────────────────────────────

public sealed record AddLibraryValueCommand(Guid FieldId, string Value) : IRequest<LibraryFieldDto>;

public sealed class AddLibraryValueCommandValidator : AbstractValidator<AddLibraryValueCommand>
{
    public AddLibraryValueCommandValidator()
    {
        RuleFor(x => x.FieldId).NotEmpty();
        RuleFor(x => x.Value).NotEmpty().WithMessage("Value is required.").MaximumLength(100);
    }
}

public sealed class AddLibraryValueCommandHandler : IRequestHandler<AddLibraryValueCommand, LibraryFieldDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ISender _sender;

    public AddLibraryValueCommandHandler(IRowDbContext db, IPagePermissionService permissions, ISender sender)
    {
        _db = db;
        _permissions = permissions;
        _sender = sender;
    }

    public async Task<LibraryFieldDto> Handle(AddLibraryValueCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Library, PermissionAction.Create, ct);
        var field = await LibraryRules.LoadAsync(_db, request.FieldId, ct);
        LibraryRules.EnsureList(field);
        var value = request.Value.Trim();

        if (LibraryRules.IsParcelStatus(field))
        {
            var next = (await _db.ParcelStatuses.MaxAsync(s => (int?)s.SortOrder, ct) ?? -1) + 1;
            await _sender.Send(new SaveParcelStatusCommand(null, value, DefaultParcelRules.NeutralColor, next, true, false), ct);
            return await LibraryRules.ToDtoAsync(_db, field, ct);
        }

        var normalized = LibraryRules.Normalize(value);
        if (await _db.LibraryValues.AnyAsync(v => v.LibraryFieldId == field.Id && v.NormalizedValue == normalized, ct))
            throw new ConflictException("library_value_exists", $"'{value}' is already in {field.Label}.");

        _db.LibraryValues.Add(new LibraryValue
        {
            LibraryFieldId = field.Id, Value = value, NormalizedValue = normalized,
            SortOrder = (await _db.LibraryValues.Where(v => v.LibraryFieldId == field.Id).MaxAsync(v => (int?)v.SortOrder, ct) ?? -1) + 1,
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("library_value_exists", $"'{value}' is already in {field.Label}.");
        }
        return await LibraryRules.ToDtoAsync(_db, field, ct);
    }
}

public sealed record UpdateLibraryValueCommand(Guid FieldId, Guid ValueId, string Value, bool IsActive) : IRequest<LibraryFieldDto>;

public sealed class UpdateLibraryValueCommandValidator : AbstractValidator<UpdateLibraryValueCommand>
{
    public UpdateLibraryValueCommandValidator()
    {
        RuleFor(x => x.FieldId).NotEmpty();
        RuleFor(x => x.ValueId).NotEmpty();
        RuleFor(x => x.Value).NotEmpty().WithMessage("Value is required.").MaximumLength(100);
    }
}

public sealed class UpdateLibraryValueCommandHandler : IRequestHandler<UpdateLibraryValueCommand, LibraryFieldDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ISender _sender;

    public UpdateLibraryValueCommandHandler(IRowDbContext db, IPagePermissionService permissions, ISender sender)
    {
        _db = db;
        _permissions = permissions;
        _sender = sender;
    }

    public async Task<LibraryFieldDto> Handle(UpdateLibraryValueCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Library, PermissionAction.Update, ct);
        var field = await LibraryRules.LoadAsync(_db, request.FieldId, ct);
        var value = request.Value.Trim();

        if (LibraryRules.IsParcelStatus(field))
        {
            var status = await _db.ParcelStatuses.FirstOrDefaultAsync(s => s.Id == request.ValueId, ct) ?? throw new NotFoundException("Library value", request.ValueId);
            await _sender.Send(new SaveParcelStatusCommand(status.Id, value, status.Color, status.SortOrder, request.IsActive, status.IsInitial), ct);
            return await LibraryRules.ToDtoAsync(_db, field, ct);
        }

        var row = await _db.LibraryValues.FirstOrDefaultAsync(v => v.Id == request.ValueId && v.LibraryFieldId == field.Id, ct)
                  ?? throw new NotFoundException("Library value", request.ValueId);
        var normalized = LibraryRules.Normalize(value);
        if (await _db.LibraryValues.AnyAsync(v => v.LibraryFieldId == field.Id && v.NormalizedValue == normalized && v.Id != row.Id, ct))
            throw new ConflictException("library_value_exists", $"'{value}' is already in {field.Label}.");
        row.Value = value;
        row.NormalizedValue = normalized;
        row.IsActive = request.IsActive;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("library_value_exists", $"'{value}' is already in {field.Label}.");
        }
        return await LibraryRules.ToDtoAsync(_db, field, ct);
    }
}

/// <summary>Adds many values at once (an Excel/CSV import). Values already in the list are skipped, never changed or duplicated.</summary>
public sealed record ImportLibraryValuesCommand(Guid FieldId, IReadOnlyList<string> Values) : IRequest<LibraryImportResultDto>;

public sealed class ImportLibraryValuesCommandValidator : AbstractValidator<ImportLibraryValuesCommand>
{
    public ImportLibraryValuesCommandValidator()
    {
        RuleFor(x => x.FieldId).NotEmpty();
        RuleFor(x => x.Values).NotNull().Must(v => v is { Count: > 0 }).WithMessage("There is nothing to import.")
            .Must(v => v is null || v.Count <= LibraryRules.MaxImport).WithMessage($"Import at most {LibraryRules.MaxImport} values at a time.");
        RuleForEach(x => x.Values).NotEmpty().WithMessage("A value cannot be empty.").MaximumLength(100).WithMessage("A value can have at most 100 characters.");
    }
}

public sealed class ImportLibraryValuesCommandHandler : IRequestHandler<ImportLibraryValuesCommand, LibraryImportResultDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ISender _sender;

    public ImportLibraryValuesCommandHandler(IRowDbContext db, IPagePermissionService permissions, ISender sender)
    {
        _db = db;
        _permissions = permissions;
        _sender = sender;
    }

    public async Task<LibraryImportResultDto> Handle(ImportLibraryValuesCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Library, PermissionAction.Update, ct);
        var field = await LibraryRules.LoadAsync(_db, request.FieldId, ct);
        LibraryRules.EnsureList(field);

        var wanted = new List<string>();
        var seen = new HashSet<string>();
        foreach (var raw in request.Values)
            if (seen.Add(LibraryRules.Normalize(raw))) wanted.Add(raw.Trim());
        var skipped = request.Values.Count - wanted.Count;

        if (LibraryRules.IsParcelStatus(field))
        {
            var have = (await _db.ParcelStatuses.Select(s => s.Name).ToListAsync(ct)).Select(LibraryRules.Normalize).ToHashSet();
            var next = (await _db.ParcelStatuses.MaxAsync(s => (int?)s.SortOrder, ct) ?? -1) + 1;
            var added = 0;
            foreach (var v in wanted)
            {
                if (have.Contains(LibraryRules.Normalize(v))) { skipped++; continue; }
                await _sender.Send(new SaveParcelStatusCommand(null, v, DefaultParcelRules.NeutralColor, next++, true, false), ct);
                added++;
            }
            return new LibraryImportResultDto(added, skipped, await LibraryRules.ToDtoAsync(_db, field, ct));
        }

        var existing = (await _db.LibraryValues.Where(v => v.LibraryFieldId == field.Id).Select(v => v.NormalizedValue).ToListAsync(ct)).ToHashSet();
        var order = (await _db.LibraryValues.Where(v => v.LibraryFieldId == field.Id).MaxAsync(v => (int?)v.SortOrder, ct) ?? -1) + 1;
        var count = 0;
        foreach (var v in wanted)
        {
            var n = LibraryRules.Normalize(v);
            if (existing.Contains(n)) { skipped++; continue; }
            _db.LibraryValues.Add(new LibraryValue { LibraryFieldId = field.Id, Value = v, NormalizedValue = n, SortOrder = order++ });
            count++;
        }
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("library_value_exists", "Someone added one of these values at the same moment. Please try again.");
        }
        return new LibraryImportResultDto(count, skipped, await LibraryRules.ToDtoAsync(_db, field, ct));
    }
}
