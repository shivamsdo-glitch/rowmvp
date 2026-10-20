using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.ParcelRecords;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.ParcelTabs;

// ── Easements (Phase 1.5, ADR 0017) ─────────────────────────────────────

public sealed record EasementDto(
    Guid Id, Guid ParcelId, string Number, string? EasementType, string? Description, DateOnly? StartDate, DateOnly? EndDate,
    string? StartStation, string? EndStation, DateTime UpdatedAt);

internal static class EasementRules
{
    public static EasementDto ToDto(ParcelEasement e) =>
        new(e.Id, e.ParcelId, e.Number, e.EasementType, e.Description, e.StartDate, e.EndDate, e.StartStation, e.EndStation, e.UpdatedAt);

    public static ConflictException Duplicate(string number) =>
        new("easement_number_exists", $"This parcel already has an easement numbered '{number}'.");

    public static void Apply(ParcelEasement e, IEasementFields f)
    {
        e.Number = f.Number.Trim();
        e.EasementType = ParcelRecordHelpers.Clean(f.EasementType);
        e.Description = ParcelRecordHelpers.Clean(f.Description);
        e.StartDate = f.StartDate;
        e.EndDate = f.EndDate;
        e.StartStation = ParcelRecordHelpers.Clean(f.StartStation);
        e.EndStation = ParcelRecordHelpers.Clean(f.EndStation);
    }
}

public interface IEasementFields
{
    string Number { get; }
    string? EasementType { get; }
    string? Description { get; }
    DateOnly? StartDate { get; }
    DateOnly? EndDate { get; }
    string? StartStation { get; }
    string? EndStation { get; }
}

public abstract class EasementFieldsValidator<T> : AbstractValidator<T> where T : IEasementFields
{
    protected EasementFieldsValidator()
    {
        RuleFor(x => x.Number).NotEmpty().WithMessage("Easement number is required.").MaximumLength(50);
        RuleFor(x => x.EasementType).MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.StartStation).MaximumLength(100);
        RuleFor(x => x.EndStation).MaximumLength(100);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate!.Value).When(x => x.StartDate is not null && x.EndDate is not null)
            .WithMessage("The end date cannot be before the start date.");
    }
}

public sealed record ListEasementsQuery(Guid ParcelId) : IRequest<IReadOnlyList<EasementDto>>;

public sealed class ListEasementsQueryHandler : IRequestHandler<ListEasementsQuery, IReadOnlyList<EasementDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public ListEasementsQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<IReadOnlyList<EasementDto>> Handle(ListEasementsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Easements, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var rows = await _db.ParcelEasements.Where(e => e.ParcelId == parcel.Id).ToListAsync(ct);
        return rows.OrderBy(e => e.Number, StringComparer.OrdinalIgnoreCase).Select(EasementRules.ToDto).ToList();
    }
}

public sealed record AddEasementCommand(
    Guid ParcelId, string Number, string? EasementType, string? Description, DateOnly? StartDate, DateOnly? EndDate, string? StartStation, string? EndStation)
    : IRequest<EasementDto>, IEasementFields;

public sealed class AddEasementCommandValidator : EasementFieldsValidator<AddEasementCommand>
{
    public AddEasementCommandValidator() => RuleFor(x => x.ParcelId).NotEmpty();
}

public sealed class AddEasementCommandHandler : IRequestHandler<AddEasementCommand, EasementDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public AddEasementCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<EasementDto> Handle(AddEasementCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Easements, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var number = request.Number.Trim();
        if (await _db.ParcelEasements.AnyAsync(e => e.ParcelId == parcel.Id && e.Number == number, ct)) throw EasementRules.Duplicate(number);

        var easement = new ParcelEasement { ParcelId = parcel.Id };
        EasementRules.Apply(easement, request);
        _db.ParcelEasements.Add(easement);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw EasementRules.Duplicate(number);
        }
        return EasementRules.ToDto(easement);
    }
}

public sealed record UpdateEasementCommand(
    Guid ParcelId, Guid EasementId, string Number, string? EasementType, string? Description, DateOnly? StartDate, DateOnly? EndDate, string? StartStation, string? EndStation)
    : IRequest<EasementDto>, IEasementFields;

public sealed class UpdateEasementCommandValidator : EasementFieldsValidator<UpdateEasementCommand>
{
    public UpdateEasementCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.EasementId).NotEmpty();
    }
}

public sealed class UpdateEasementCommandHandler : IRequestHandler<UpdateEasementCommand, EasementDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public UpdateEasementCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<EasementDto> Handle(UpdateEasementCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Easements, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var easement = await _db.ParcelEasements.FirstOrDefaultAsync(e => e.Id == request.EasementId && e.ParcelId == parcel.Id, ct)
                       ?? throw new NotFoundException("Easement", request.EasementId);
        var number = request.Number.Trim();
        if (await _db.ParcelEasements.AnyAsync(e => e.ParcelId == parcel.Id && e.Number == number && e.Id != easement.Id, ct)) throw EasementRules.Duplicate(number);

        EasementRules.Apply(easement, request);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw EasementRules.Duplicate(number);
        }
        return EasementRules.ToDto(easement);
    }
}

public sealed record RemoveEasementCommand(Guid ParcelId, Guid EasementId) : IRequest;

public sealed class RemoveEasementCommandHandler : IRequestHandler<RemoveEasementCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveEasementCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveEasementCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Easements, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var easement = await _db.ParcelEasements.FirstOrDefaultAsync(e => e.Id == request.EasementId && e.ParcelId == parcel.Id, ct)
                       ?? throw new NotFoundException("Easement", request.EasementId);
        _db.ParcelEasements.Remove(easement);
        await _db.SaveChangesAsync(ct);
    }
}
