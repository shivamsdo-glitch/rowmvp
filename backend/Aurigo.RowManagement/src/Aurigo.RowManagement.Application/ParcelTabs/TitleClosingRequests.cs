using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.ParcelRecords;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Parties;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.ParcelTabs;

// ── Title and Closing (Phase 1.5, ADR 0018) ─────────────────────────────

public sealed record ConveyanceDocumentInput(string? ConveyanceType, DateOnly? SignedDate, DateOnly? RecordedDate, string? RecordingNumber);
public sealed record TitleCommitmentInput(
    string VestedOwnerName, DateOnly? RequestedDate, DateOnly? ReceivedDate, DateOnly? EffectiveDate, DateOnly? IssuedDate,
    DateOnly? CurativePlanSubmittedDate, DateOnly? CurativePlanApprovedDate);

public sealed record ConveyanceDocumentDto(Guid Id, string? ConveyanceType, DateOnly? SignedDate, DateOnly? RecordedDate, string? RecordingNumber);
public sealed record TitleCommitmentDto(
    Guid Id, string VestedOwnerName, DateOnly? RequestedDate, DateOnly? ReceivedDate, DateOnly? EffectiveDate, DateOnly? IssuedDate,
    DateOnly? CurativePlanSubmittedDate, DateOnly? CurativePlanApprovedDate);

/// <summary><see cref="VestedOwnerName"/> is read-only: the parcel's ownerships of type "Vested Owner".</summary>
public sealed record TitleClosingDto(
    Guid Id, Guid ParcelId, string Number, string ParcelNumber, string? VestedOwnerName, string? AssignedTo, string? TitleCompany, string? TitleClosingAgent,
    string? GfNumber, string? FinalTitlePolicyNo, DateOnly? PsaSignedByOwnerDate, DateOnly? PsaSignedByAtpDate, decimal? PsaAmount,
    DateOnly? SettlementReceivedDate, DateOnly? SettlementExecutedDate, bool CurativeItemsCompleted, DateOnly? ClosingPackageSubmittedDate,
    DateOnly? ClosingPackageApprovedDate, DateOnly? ClosingInstructionDate, DateOnly? AnticipatedClosingDate, DateOnly? ActualClosingDate,
    IReadOnlyList<ConveyanceDocumentDto> ConveyanceDocuments, IReadOnlyList<TitleCommitmentDto> TitleCommitments, DateTime UpdatedAt);

internal static class TitleClosingRules
{
    public const string VestedOwnerType = "Vested Owner";
    public const decimal MaxAmount = 1_000_000_000_000m;

    public static async Task<string?> VestedOwnerNameAsync(IRowDbContext db, Guid parcelId, CancellationToken ct)
    {
        return await ParcelOwners.JoinedAsync(db, parcelId, VestedOwnerType, ct);
    }

    public static TitleClosingDto ToDto(ParcelTitleClosing t, string parcelNumber, string? vested) =>
        new(t.Id, t.ParcelId, t.Number, parcelNumber, vested, t.AssignedTo, t.TitleCompany, t.TitleClosingAgent, t.GfNumber, t.FinalTitlePolicyNo,
            t.PsaSignedByOwnerDate, t.PsaSignedByAtpDate, t.PsaAmount, t.SettlementReceivedDate, t.SettlementExecutedDate, t.CurativeItemsCompleted,
            t.ClosingPackageSubmittedDate, t.ClosingPackageApprovedDate, t.ClosingInstructionDate, t.AnticipatedClosingDate, t.ActualClosingDate,
            t.ConveyanceDocuments.OrderBy(c => c.CreatedAt).ThenBy(c => c.ConveyanceType).Select(c => new ConveyanceDocumentDto(c.Id, c.ConveyanceType, c.SignedDate, c.RecordedDate, c.RecordingNumber)).ToList(),
            t.TitleCommitments.OrderBy(c => c.CreatedAt).ThenBy(c => c.VestedOwnerName).Select(c => new TitleCommitmentDto(c.Id, c.VestedOwnerName, c.RequestedDate, c.ReceivedDate, c.EffectiveDate, c.IssuedDate, c.CurativePlanSubmittedDate, c.CurativePlanApprovedDate)).ToList(),
            t.UpdatedAt);

    public static void Apply(ParcelTitleClosing t, ITitleClosingFields f)
    {
        t.AssignedTo = ParcelRecordHelpers.Clean(f.AssignedTo);
        t.TitleCompany = ParcelRecordHelpers.Clean(f.TitleCompany);
        t.TitleClosingAgent = ParcelRecordHelpers.Clean(f.TitleClosingAgent);
        t.GfNumber = ParcelRecordHelpers.Clean(f.GfNumber);
        t.FinalTitlePolicyNo = ParcelRecordHelpers.Clean(f.FinalTitlePolicyNo);
        t.PsaSignedByOwnerDate = f.PsaSignedByOwnerDate;
        t.PsaSignedByAtpDate = f.PsaSignedByAtpDate;
        t.PsaAmount = f.PsaAmount;
        t.SettlementReceivedDate = f.SettlementReceivedDate;
        t.SettlementExecutedDate = f.SettlementExecutedDate;
        t.CurativeItemsCompleted = f.CurativeItemsCompleted;
        t.ClosingPackageSubmittedDate = f.ClosingPackageSubmittedDate;
        t.ClosingPackageApprovedDate = f.ClosingPackageApprovedDate;
        t.ClosingInstructionDate = f.ClosingInstructionDate;
        t.AnticipatedClosingDate = f.AnticipatedClosingDate;
        t.ActualClosingDate = f.ActualClosingDate;
    }

    public static List<ParcelConveyanceDocument> BuildConveyances(IReadOnlyList<ConveyanceDocumentInput>? input) =>
        (input ?? Array.Empty<ConveyanceDocumentInput>()).Select(c => new ParcelConveyanceDocument
        {
            ConveyanceType = ParcelRecordHelpers.Clean(c.ConveyanceType), SignedDate = c.SignedDate, RecordedDate = c.RecordedDate, RecordingNumber = ParcelRecordHelpers.Clean(c.RecordingNumber),
        }).ToList();

    public static List<ParcelTitleCommitment> BuildCommitments(IReadOnlyList<TitleCommitmentInput>? input) =>
        (input ?? Array.Empty<TitleCommitmentInput>()).Select(c => new ParcelTitleCommitment
        {
            VestedOwnerName = c.VestedOwnerName.Trim(), RequestedDate = c.RequestedDate, ReceivedDate = c.ReceivedDate, EffectiveDate = c.EffectiveDate, IssuedDate = c.IssuedDate,
            CurativePlanSubmittedDate = c.CurativePlanSubmittedDate, CurativePlanApprovedDate = c.CurativePlanApprovedDate,
        }).ToList();
}

public interface ITitleClosingFields
{
    string? AssignedTo { get; }
    string? TitleCompany { get; }
    string? TitleClosingAgent { get; }
    string? GfNumber { get; }
    string? FinalTitlePolicyNo { get; }
    DateOnly? PsaSignedByOwnerDate { get; }
    DateOnly? PsaSignedByAtpDate { get; }
    decimal? PsaAmount { get; }
    DateOnly? SettlementReceivedDate { get; }
    DateOnly? SettlementExecutedDate { get; }
    bool CurativeItemsCompleted { get; }
    DateOnly? ClosingPackageSubmittedDate { get; }
    DateOnly? ClosingPackageApprovedDate { get; }
    DateOnly? ClosingInstructionDate { get; }
    DateOnly? AnticipatedClosingDate { get; }
    DateOnly? ActualClosingDate { get; }
    IReadOnlyList<ConveyanceDocumentInput>? ConveyanceDocuments { get; }
    IReadOnlyList<TitleCommitmentInput>? TitleCommitments { get; }
}

public abstract class TitleClosingFieldsValidator<T> : AbstractValidator<T> where T : ITitleClosingFields
{
    protected TitleClosingFieldsValidator()
    {
        RuleFor(x => x.AssignedTo).MaximumLength(200);
        RuleFor(x => x.TitleCompany).MaximumLength(200);
        RuleFor(x => x.TitleClosingAgent).MaximumLength(200);
        RuleFor(x => x.GfNumber).MaximumLength(100);
        RuleFor(x => x.FinalTitlePolicyNo).MaximumLength(100);
        RuleFor(x => x.PsaAmount).InclusiveBetween(0m, TitleClosingRules.MaxAmount).When(x => x.PsaAmount is not null).WithMessage("The amount must be zero or more.");
        RuleFor(x => x.SettlementExecutedDate).GreaterThanOrEqualTo(x => x.SettlementReceivedDate!.Value)
            .When(x => x.SettlementReceivedDate is not null && x.SettlementExecutedDate is not null).WithMessage("The statement cannot be executed before it is received.");
        RuleFor(x => x.ClosingPackageApprovedDate).GreaterThanOrEqualTo(x => x.ClosingPackageSubmittedDate!.Value)
            .When(x => x.ClosingPackageSubmittedDate is not null && x.ClosingPackageApprovedDate is not null).WithMessage("The closing package cannot be approved before it is submitted.");

        RuleFor(x => x.ConveyanceDocuments).Must(c => c is null || c.Count <= 50).WithMessage("A parcel can have at most 50 conveyance documents.");
        RuleForEach(x => x.ConveyanceDocuments).ChildRules(c =>
        {
            c.RuleFor(i => i.ConveyanceType).MaximumLength(100);
            c.RuleFor(i => i.RecordingNumber).MaximumLength(100);
            c.RuleFor(i => i.RecordedDate).GreaterThanOrEqualTo(i => i.SignedDate!.Value).When(i => i.SignedDate is not null && i.RecordedDate is not null)
                .WithMessage("A conveyance cannot be recorded before it is signed.");
        });
        RuleFor(x => x.TitleCommitments).Must(c => c is null || c.Count <= 50).WithMessage("A parcel can have at most 50 title commitments.");
        RuleForEach(x => x.TitleCommitments).ChildRules(c =>
        {
            c.RuleFor(i => i.VestedOwnerName).NotEmpty().WithMessage("Each title commitment needs a vested owner name.").MaximumLength(200);
            c.RuleFor(i => i.CurativePlanApprovedDate).GreaterThanOrEqualTo(i => i.CurativePlanSubmittedDate!.Value)
                .When(i => i.CurativePlanSubmittedDate is not null && i.CurativePlanApprovedDate is not null).WithMessage("A curative plan cannot be approved before it is submitted.");
        });
    }
}

public sealed record GetTitleClosingQuery(Guid ParcelId) : IRequest<TitleClosingDto?>;

public sealed class GetTitleClosingQueryHandler : IRequestHandler<GetTitleClosingQuery, TitleClosingDto?>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public GetTitleClosingQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<TitleClosingDto?> Handle(GetTitleClosingQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.TitleClosing, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var t = await _db.ParcelTitleClosings.Include(x => x.ConveyanceDocuments).Include(x => x.TitleCommitments).AsNoTracking().FirstOrDefaultAsync(x => x.ParcelId == parcel.Id, ct);
        return t is null ? null : TitleClosingRules.ToDto(t, parcel.Number, await TitleClosingRules.VestedOwnerNameAsync(_db, parcel.Id, ct));
    }
}

/// <summary>Saves the parcel's title and closing record: creates it the first time (needs Create) and changes it after that (needs Update).</summary>
public sealed record SaveTitleClosingCommand(
    Guid ParcelId, string? AssignedTo, string? TitleCompany, string? TitleClosingAgent, string? GfNumber, string? FinalTitlePolicyNo,
    DateOnly? PsaSignedByOwnerDate, DateOnly? PsaSignedByAtpDate, decimal? PsaAmount, DateOnly? SettlementReceivedDate, DateOnly? SettlementExecutedDate,
    bool CurativeItemsCompleted, DateOnly? ClosingPackageSubmittedDate, DateOnly? ClosingPackageApprovedDate, DateOnly? ClosingInstructionDate,
    DateOnly? AnticipatedClosingDate, DateOnly? ActualClosingDate, IReadOnlyList<ConveyanceDocumentInput>? ConveyanceDocuments, IReadOnlyList<TitleCommitmentInput>? TitleCommitments)
    : IRequest<TitleClosingDto>, ITitleClosingFields;

public sealed class SaveTitleClosingCommandValidator : TitleClosingFieldsValidator<SaveTitleClosingCommand>
{
    public SaveTitleClosingCommandValidator() => RuleFor(x => x.ParcelId).NotEmpty();
}

public sealed class SaveTitleClosingCommandHandler : IRequestHandler<SaveTitleClosingCommand, TitleClosingDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public SaveTitleClosingCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<TitleClosingDto> Handle(SaveTitleClosingCommand request, CancellationToken ct)
    {
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var existing = await _db.ParcelTitleClosings.Include(x => x.ConveyanceDocuments).Include(x => x.TitleCommitments).FirstOrDefaultAsync(x => x.ParcelId == parcel.Id, ct);
        await _permissions.EnsureAsync(PageKeys.TitleClosing, existing is null ? PermissionAction.Create : PermissionAction.Update, ct);

        var conveyances = TitleClosingRules.BuildConveyances(request.ConveyanceDocuments);
        var commitments = TitleClosingRules.BuildCommitments(request.TitleCommitments);
        ParcelTitleClosing record;
        if (existing is null)
        {
            record = new ParcelTitleClosing { ParcelId = parcel.Id, Number = $"TC-{parcel.Number}", ConveyanceDocuments = conveyances, TitleCommitments = commitments };
            TitleClosingRules.Apply(record, request);
            _db.ParcelTitleClosings.Add(record);
        }
        else
        {
            record = existing;
            TitleClosingRules.Apply(record, request);
            _db.ParcelConveyanceDocuments.RemoveRange(record.ConveyanceDocuments);
            _db.ParcelTitleCommitments.RemoveRange(record.TitleCommitments);
            foreach (var c in conveyances) c.TitleClosingId = record.Id;
            foreach (var c in commitments) c.TitleClosingId = record.Id;
            _db.ParcelConveyanceDocuments.AddRange(conveyances);
            _db.ParcelTitleCommitments.AddRange(commitments);
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // Two people saved the first version at once; the other one won.
            _db.ChangeTracker.Clear();
            throw new ConflictException("title_closing_exists", "Someone saved this parcel's title and closing record at the same moment. Reload it and try again.");
        }

        var fresh = await _db.ParcelTitleClosings.Include(x => x.ConveyanceDocuments).Include(x => x.TitleCommitments).AsNoTracking().FirstAsync(x => x.Id == record.Id, ct);
        return TitleClosingRules.ToDto(fresh, parcel.Number, await TitleClosingRules.VestedOwnerNameAsync(_db, parcel.Id, ct));
    }
}

public sealed record RemoveTitleClosingCommand(Guid ParcelId) : IRequest;

public sealed class RemoveTitleClosingCommandHandler : IRequestHandler<RemoveTitleClosingCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveTitleClosingCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveTitleClosingCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.TitleClosing, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var t = await _db.ParcelTitleClosings.Include(x => x.ConveyanceDocuments).Include(x => x.TitleCommitments).FirstOrDefaultAsync(x => x.ParcelId == parcel.Id, ct)
                ?? throw new NotFoundException("Title and closing record", parcel.Id);
        _db.ParcelConveyanceDocuments.RemoveRange(t.ConveyanceDocuments);
        _db.ParcelTitleCommitments.RemoveRange(t.TitleCommitments);
        _db.ParcelTitleClosings.Remove(t);
        await _db.SaveChangesAsync(ct);
    }
}
