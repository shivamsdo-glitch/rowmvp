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

// ── Property Management & Disposition (Phase 1.5, ADR 0020) ─────────────

public sealed record PropertyFundingInput(string FundType, string? FundSource, decimal? Amount, decimal? Percentage);
public sealed record PropertyFundingDto(Guid Id, string FundType, string? FundSource, decimal? Amount, decimal? Percentage);

public sealed record PropertyDetails(
    DateOnly? TransferMemoSubmittedToAtp, DateOnly? TransferMemoApprovedByAtp, DateOnly? TransferMemoToPdb, DateOnly? PropertySecured, string? DemoStatus, DateOnly? DemoCompleted,
    string? DispositionType, string? DispositionMethod, string? DispositionStatus, string? DispositionJustification, string? BuyerGrantee, bool FtaConcurrenceRequired,
    decimal? SalePrice, DateOnly? LegalReviewCompleteDate, DateOnly? BoardApprovalDate, DateOnly? ClosingTransferDate, DateOnly? RecordingDate, string? DocumentRecordingNo);

public sealed record PropertyDto(
    Guid Id, Guid ParcelId, string Number, string ParcelNumber, PropertyDetails Details, IReadOnlyList<StatusNoteDto> StatusNotes,
    IReadOnlyList<PropertyFundingDto> Funding, DateTime UpdatedAt);

internal static class PropertyRules
{
    public const decimal MaxAmount = 1_000_000_000_000m;

    public static PropertyDetails DetailsOf(ParcelPropertyRecord p) => new(
        p.TransferMemoSubmittedToAtp, p.TransferMemoApprovedByAtp, p.TransferMemoToPdb, p.PropertySecured, p.DemoStatus, p.DemoCompleted, p.DispositionType, p.DispositionMethod,
        p.DispositionStatus, p.DispositionJustification, p.BuyerGrantee, p.FtaConcurrenceRequired, p.SalePrice, p.LegalReviewCompleteDate, p.BoardApprovalDate,
        p.ClosingTransferDate, p.RecordingDate, p.DocumentRecordingNo);

    public static async Task<PropertyDto> ToDtoAsync(IRowDbContext db, ParcelPropertyRecord p, string parcelNumber, CancellationToken ct) =>
        new(p.Id, p.ParcelId, p.Number, parcelNumber, DetailsOf(p), await StatusNoteRules.LoadAsync(db, StatusNoteKinds.PropertyManagement, p.Id, ct),
            p.Funding.OrderBy(f => f.CreatedAt).Select(f => new PropertyFundingDto(f.Id, f.FundType, f.FundSource, f.Amount, f.Percentage)).ToList(), p.UpdatedAt);

    public static void Apply(ParcelPropertyRecord p, PropertyDetails d)
    {
        p.TransferMemoSubmittedToAtp = d.TransferMemoSubmittedToAtp; p.TransferMemoApprovedByAtp = d.TransferMemoApprovedByAtp; p.TransferMemoToPdb = d.TransferMemoToPdb;
        p.PropertySecured = d.PropertySecured; p.DemoStatus = ParcelRecordHelpers.Clean(d.DemoStatus); p.DemoCompleted = d.DemoCompleted;
        p.DispositionType = ParcelRecordHelpers.Clean(d.DispositionType); p.DispositionMethod = ParcelRecordHelpers.Clean(d.DispositionMethod);
        p.DispositionStatus = ParcelRecordHelpers.Clean(d.DispositionStatus); p.DispositionJustification = ParcelRecordHelpers.Clean(d.DispositionJustification);
        p.BuyerGrantee = ParcelRecordHelpers.Clean(d.BuyerGrantee); p.FtaConcurrenceRequired = d.FtaConcurrenceRequired; p.SalePrice = d.SalePrice;
        p.LegalReviewCompleteDate = d.LegalReviewCompleteDate; p.BoardApprovalDate = d.BoardApprovalDate; p.ClosingTransferDate = d.ClosingTransferDate;
        p.RecordingDate = d.RecordingDate; p.DocumentRecordingNo = ParcelRecordHelpers.Clean(d.DocumentRecordingNo);
    }

    public static List<PropertyFunding> BuildFunding(IReadOnlyList<PropertyFundingInput>? input) =>
        (input ?? Array.Empty<PropertyFundingInput>()).Select(f => new PropertyFunding
        {
            FundType = f.FundType.Trim(), FundSource = ParcelRecordHelpers.Clean(f.FundSource), Amount = f.Amount, Percentage = f.Percentage,
        }).ToList();
}

public sealed record GetPropertyQuery(Guid ParcelId) : IRequest<PropertyDto?>;

public sealed class GetPropertyQueryHandler : IRequestHandler<GetPropertyQuery, PropertyDto?>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public GetPropertyQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<PropertyDto?> Handle(GetPropertyQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelProperty, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var p = await _db.ParcelPropertyRecords.Include(x => x.Funding).AsNoTracking().FirstOrDefaultAsync(x => x.ParcelId == parcel.Id, ct);
        return p is null ? null : await PropertyRules.ToDtoAsync(_db, p, parcel.Number, ct);
    }
}

/// <summary>Saves the parcel's property management and disposition record: creates it the first time (needs Create) and changes it after that (needs Update).</summary>
public sealed record SavePropertyCommand(Guid ParcelId, PropertyDetails Details, IReadOnlyList<StatusNoteInput>? StatusNotes, IReadOnlyList<PropertyFundingInput>? Funding) : IRequest<PropertyDto>;

public sealed class SavePropertyCommandValidator : AbstractValidator<SavePropertyCommand>
{
    public SavePropertyCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.Details).NotNull().WithMessage("The property details are required.");
        When(x => x.Details is not null, () =>
        {
            RuleFor(x => x.Details.DemoStatus).MaximumLength(100);
            RuleFor(x => x.Details.DispositionType).MaximumLength(100);
            RuleFor(x => x.Details.DispositionMethod).MaximumLength(100);
            RuleFor(x => x.Details.DispositionStatus).MaximumLength(100);
            RuleFor(x => x.Details.DispositionJustification).MaximumLength(4000);
            RuleFor(x => x.Details.BuyerGrantee).MaximumLength(1000);
            RuleFor(x => x.Details.DocumentRecordingNo).MaximumLength(1000);
            RuleFor(x => x.Details.SalePrice).InclusiveBetween(0m, PropertyRules.MaxAmount).When(x => x.Details.SalePrice is not null).WithMessage("The amount must be zero or more.");
            RuleFor(x => x.Details.TransferMemoApprovedByAtp).GreaterThanOrEqualTo(x => x.Details.TransferMemoSubmittedToAtp!.Value)
                .When(x => x.Details.TransferMemoSubmittedToAtp is not null && x.Details.TransferMemoApprovedByAtp is not null).WithMessage("The memo cannot be approved before it is submitted.");
            RuleFor(x => x.Details.RecordingDate).GreaterThanOrEqualTo(x => x.Details.ClosingTransferDate!.Value)
                .When(x => x.Details.ClosingTransferDate is not null && x.Details.RecordingDate is not null).WithMessage("The deed cannot be recorded before the closing or transfer date.");
        });
        StatusNoteRules.AddRules(this, x => x.StatusNotes);
        RuleFor(x => x.Funding).Must(r => r is null || r.Count <= 50).WithMessage("At most 50 funding rows.");
        RuleForEach(x => x.Funding).ChildRules(r =>
        {
            r.RuleFor(i => i.FundType).NotEmpty().WithMessage("Each funding row needs a fund type.").MaximumLength(100);
            r.RuleFor(i => i.FundSource).MaximumLength(200);
            r.RuleFor(i => i.Amount).InclusiveBetween(0m, PropertyRules.MaxAmount).When(i => i.Amount is not null).WithMessage("The amount must be zero or more.");
            r.RuleFor(i => i.Percentage).InclusiveBetween(0m, 100m).When(i => i.Percentage is not null).WithMessage("The percentage must be between 0 and 100.");
        });
        RuleFor(x => x.Funding).Must(r => r is null || r.Sum(i => i.Percentage ?? 0m) <= 100m).WithMessage("The funding percentages cannot add up to more than 100.");
    }
}

public sealed class SavePropertyCommandHandler : IRequestHandler<SavePropertyCommand, PropertyDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public SavePropertyCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<PropertyDto> Handle(SavePropertyCommand request, CancellationToken ct)
    {
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var existing = await _db.ParcelPropertyRecords.Include(x => x.Funding).FirstOrDefaultAsync(x => x.ParcelId == parcel.Id, ct);
        await _permissions.EnsureAsync(PageKeys.ParcelProperty, existing is null ? PermissionAction.Create : PermissionAction.Update, ct);

        var funding = PropertyRules.BuildFunding(request.Funding);
        var record = existing ?? new ParcelPropertyRecord { ParcelId = parcel.Id, Number = $"PMD-{parcel.Number}" };
        PropertyRules.Apply(record, request.Details);
        if (existing is null)
        {
            record.Funding = funding;
            _db.ParcelPropertyRecords.Add(record);
        }
        else
        {
            _db.PropertyFundings.RemoveRange(record.Funding);
            foreach (var f in funding) f.PropertyRecordId = record.Id;
            _db.PropertyFundings.AddRange(funding);
        }
        await StatusNoteRules.ReplaceAsync(_db, StatusNoteKinds.PropertyManagement, record.Id, request.StatusNotes, ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (existing is null)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("property_record_exists", "Someone saved this parcel's property management record at the same moment. Reload it and try again.");
        }

        var fresh = await _db.ParcelPropertyRecords.Include(x => x.Funding).AsNoTracking().FirstAsync(x => x.Id == record.Id, ct);
        return await PropertyRules.ToDtoAsync(_db, fresh, parcel.Number, ct);
    }
}

public sealed record RemovePropertyCommand(Guid ParcelId) : IRequest;

public sealed class RemovePropertyCommandHandler : IRequestHandler<RemovePropertyCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemovePropertyCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemovePropertyCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelProperty, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var p = await _db.ParcelPropertyRecords.Include(x => x.Funding).FirstOrDefaultAsync(x => x.ParcelId == parcel.Id, ct)
                ?? throw new NotFoundException("Property management record", parcel.Id);
        _db.PropertyFundings.RemoveRange(p.Funding);
        await StatusNoteRules.DeleteAsync(_db, StatusNoteKinds.PropertyManagement, p.Id, ct);
        _db.ParcelPropertyRecords.Remove(p);
        await _db.SaveChangesAsync(ct);
    }
}
