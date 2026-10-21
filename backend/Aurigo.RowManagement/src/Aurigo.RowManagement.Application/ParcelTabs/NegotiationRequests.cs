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

// ── Negotiation (Phase 1.5, ADR 0019; rights of entry and agents moved out in ADR 0023) ───────────────────────────────────

public sealed record NegotiationNoteInput(DateOnly NoteDate, string Text);
public sealed record NegotiationOfferInput(string? OfferType, DateOnly? MailedDate, DateOnly? DeliveredDate, decimal? Amount, string? Notes);
public sealed record NegotiationSettlementInput(
    string? SettlementType, decimal? CounterofferAmount, DateOnly? DateReceived, DateOnly? DateSubmitted, string? CounterofferResponse,
    DateOnly? ResponseDateFromAtp, DateOnly? ResponseDateToOwner, string? SettlementNotes);

public sealed record NegotiationNoteDto(Guid Id, DateOnly NoteDate, string Text);
public sealed record NegotiationOfferDto(Guid Id, string? OfferType, DateOnly? MailedDate, DateOnly? DeliveredDate, decimal? Amount, string? Notes);
public sealed record NegotiationSettlementDto(
    Guid Id, string? SettlementType, decimal? CounterofferAmount, DateOnly? DateReceived, DateOnly? DateSubmitted, string? CounterofferResponse,
    DateOnly? ResponseDateFromAtp, DateOnly? ResponseDateToOwner, string? SettlementNotes);

public sealed record NegotiationDto(
    Guid Id, Guid ParcelId, string Number, string ParcelNumber, string? AssignedTo, decimal? TotalAgreementAmount, string? Notes,
    DateOnly? CounterofferSubmittedToJatDate, DateOnly? CounterofferResponseFromJatDate, string? JatResponse, bool FtaApprovalRequiredForCounteroffer,
    DateOnly? PuaRequestedDate, DateOnly? PuaApprovedDate, decimal? PuaApprovedAmount, DateOnly? PuaExecutedDate, DateOnly? PuaClosedDate,
    IReadOnlyList<NegotiationNoteDto> StatusNotes, IReadOnlyList<NegotiationOfferDto> Offers, IReadOnlyList<NegotiationSettlementDto> Settlements, DateTime UpdatedAt);

internal static class NegotiationRules
{
    public const decimal MaxAmount = 1_000_000_000_000m;
    public const int MaxRows = 50;

    public static IQueryable<ParcelNegotiation> WithChildren(IQueryable<ParcelNegotiation> q) =>
        q.Include(x => x.StatusNotes).Include(x => x.Offers).Include(x => x.Settlements);

    public static NegotiationDto ToDto(ParcelNegotiation n, string parcelNumber) =>
        new(n.Id, n.ParcelId, n.Number, parcelNumber, n.AssignedTo, n.TotalAgreementAmount, n.Notes,
            n.CounterofferSubmittedToJatDate, n.CounterofferResponseFromJatDate, n.JatResponse, n.FtaApprovalRequiredForCounteroffer,
            n.PuaRequestedDate, n.PuaApprovedDate, n.PuaApprovedAmount, n.PuaExecutedDate, n.PuaClosedDate,
            n.StatusNotes.OrderBy(x => x.NoteDate).ThenBy(x => x.CreatedAt).Select(x => new NegotiationNoteDto(x.Id, x.NoteDate, x.Text)).ToList(),
            n.Offers.OrderBy(x => x.CreatedAt).Select(x => new NegotiationOfferDto(x.Id, x.OfferType, x.MailedDate, x.DeliveredDate, x.Amount, x.Notes)).ToList(),
            n.Settlements.OrderBy(x => x.CreatedAt).Select(x => new NegotiationSettlementDto(x.Id, x.SettlementType, x.CounterofferAmount, x.DateReceived, x.DateSubmitted, x.CounterofferResponse, x.ResponseDateFromAtp, x.ResponseDateToOwner, x.SettlementNotes)).ToList(),
            n.UpdatedAt);

    public static void Apply(ParcelNegotiation n, INegotiationFields f)
    {
        n.AssignedTo = ParcelRecordHelpers.Clean(f.AssignedTo);
        n.TotalAgreementAmount = f.TotalAgreementAmount;
        n.Notes = ParcelRecordHelpers.Clean(f.Notes);
        n.CounterofferSubmittedToJatDate = f.CounterofferSubmittedToJatDate;
        n.CounterofferResponseFromJatDate = f.CounterofferResponseFromJatDate;
        n.JatResponse = ParcelRecordHelpers.Clean(f.JatResponse);
        n.FtaApprovalRequiredForCounteroffer = f.FtaApprovalRequiredForCounteroffer;
        n.PuaRequestedDate = f.PuaRequestedDate;
        n.PuaApprovedDate = f.PuaApprovedDate;
        n.PuaApprovedAmount = f.PuaApprovedAmount;
        n.PuaExecutedDate = f.PuaExecutedDate;
        n.PuaClosedDate = f.PuaClosedDate;
    }

    /// <summary>The three grids as new rows ready to attach; with an id, to replace the ones on an existing record.</summary>
    public sealed record Children(
        List<NegotiationNote> Notes, List<NegotiationOffer> Offers, List<NegotiationSettlement> Settlements);

    public static Children Build(INegotiationFields f) => new(
        (f.StatusNotes ?? Array.Empty<NegotiationNoteInput>()).Select(x => new NegotiationNote { NoteDate = x.NoteDate, Text = x.Text.Trim() }).ToList(),
        (f.Offers ?? Array.Empty<NegotiationOfferInput>()).Select(x => new NegotiationOffer
        {
            OfferType = ParcelRecordHelpers.Clean(x.OfferType), MailedDate = x.MailedDate, DeliveredDate = x.DeliveredDate, Amount = x.Amount, Notes = ParcelRecordHelpers.Clean(x.Notes),
        }).ToList(),
        (f.Settlements ?? Array.Empty<NegotiationSettlementInput>()).Select(x => new NegotiationSettlement
        {
            SettlementType = ParcelRecordHelpers.Clean(x.SettlementType), CounterofferAmount = x.CounterofferAmount, DateReceived = x.DateReceived, DateSubmitted = x.DateSubmitted,
            CounterofferResponse = ParcelRecordHelpers.Clean(x.CounterofferResponse), ResponseDateFromAtp = x.ResponseDateFromAtp, ResponseDateToOwner = x.ResponseDateToOwner,
            SettlementNotes = ParcelRecordHelpers.Clean(x.SettlementNotes),
        }).ToList());

    public static void Attach(IRowDbContext db, ParcelNegotiation n, Children c)
    {
        foreach (var x in c.Notes) x.NegotiationId = n.Id;
        foreach (var x in c.Offers) x.NegotiationId = n.Id;
        foreach (var x in c.Settlements) x.NegotiationId = n.Id;
        db.NegotiationNotes.AddRange(c.Notes);
        db.NegotiationOffers.AddRange(c.Offers);
        db.NegotiationSettlements.AddRange(c.Settlements);
    }

    public static void RemoveChildren(IRowDbContext db, ParcelNegotiation n)
    {
        db.NegotiationNotes.RemoveRange(n.StatusNotes);
        db.NegotiationOffers.RemoveRange(n.Offers);
        db.NegotiationSettlements.RemoveRange(n.Settlements);
    }
}

public interface INegotiationFields
{
    string? AssignedTo { get; }
    decimal? TotalAgreementAmount { get; }
    string? Notes { get; }
    DateOnly? CounterofferSubmittedToJatDate { get; }
    DateOnly? CounterofferResponseFromJatDate { get; }
    string? JatResponse { get; }
    bool FtaApprovalRequiredForCounteroffer { get; }
    DateOnly? PuaRequestedDate { get; }
    DateOnly? PuaApprovedDate { get; }
    decimal? PuaApprovedAmount { get; }
    DateOnly? PuaExecutedDate { get; }
    DateOnly? PuaClosedDate { get; }
    IReadOnlyList<NegotiationNoteInput>? StatusNotes { get; }
    IReadOnlyList<NegotiationOfferInput>? Offers { get; }
    IReadOnlyList<NegotiationSettlementInput>? Settlements { get; }
}

public abstract class NegotiationFieldsValidator<T> : AbstractValidator<T> where T : INegotiationFields
{
    private const string Money = "The amount must be zero or more.";

    protected NegotiationFieldsValidator()
    {
        RuleFor(x => x.AssignedTo).MaximumLength(200);
        RuleFor(x => x.JatResponse).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(4000);
        RuleFor(x => x.TotalAgreementAmount).InclusiveBetween(0m, NegotiationRules.MaxAmount).When(x => x.TotalAgreementAmount is not null).WithMessage(Money);
        RuleFor(x => x.PuaApprovedAmount).InclusiveBetween(0m, NegotiationRules.MaxAmount).When(x => x.PuaApprovedAmount is not null).WithMessage(Money);
        RuleFor(x => x.CounterofferResponseFromJatDate).GreaterThanOrEqualTo(x => x.CounterofferSubmittedToJatDate!.Value)
            .When(x => x.CounterofferSubmittedToJatDate is not null && x.CounterofferResponseFromJatDate is not null).WithMessage("The JAT cannot respond before the counteroffer is submitted.");
        RuleFor(x => x.PuaApprovedDate).GreaterThanOrEqualTo(x => x.PuaRequestedDate!.Value)
            .When(x => x.PuaRequestedDate is not null && x.PuaApprovedDate is not null).WithMessage("The PUA cannot be approved before it is requested.");
        RuleFor(x => x.PuaClosedDate).GreaterThanOrEqualTo(x => x.PuaExecutedDate!.Value)
            .When(x => x.PuaExecutedDate is not null && x.PuaClosedDate is not null).WithMessage("The PUA cannot be closed before it is executed.");

        RuleFor(x => x.StatusNotes).Must(r => r is null || r.Count <= NegotiationRules.MaxRows).WithMessage("A negotiation can have at most 50 status notes.");
        RuleForEach(x => x.StatusNotes).ChildRules(r => r.RuleFor(i => i.Text).NotEmpty().WithMessage("Each status note needs text.").MaximumLength(4000));
        RuleFor(x => x.Offers).Must(r => r is null || r.Count <= NegotiationRules.MaxRows).WithMessage("A negotiation can have at most 50 offers.");
        RuleForEach(x => x.Offers).ChildRules(r =>
        {
            r.RuleFor(i => i.OfferType).MaximumLength(100);
            r.RuleFor(i => i.Notes).MaximumLength(2000);
            r.RuleFor(i => i.Amount).InclusiveBetween(0m, NegotiationRules.MaxAmount).When(i => i.Amount is not null).WithMessage(Money);
            r.RuleFor(i => i.DeliveredDate).GreaterThanOrEqualTo(i => i.MailedDate!.Value)
                .When(i => i.MailedDate is not null && i.DeliveredDate is not null).WithMessage("An offer cannot be delivered before it is mailed.");
        });
        RuleFor(x => x.Settlements).Must(r => r is null || r.Count <= NegotiationRules.MaxRows).WithMessage("A negotiation can have at most 50 settlements.");
        RuleForEach(x => x.Settlements).ChildRules(r =>
        {
            r.RuleFor(i => i.SettlementType).MaximumLength(100);
            r.RuleFor(i => i.CounterofferResponse).MaximumLength(100);
            r.RuleFor(i => i.SettlementNotes).MaximumLength(2000);
            r.RuleFor(i => i.CounterofferAmount).InclusiveBetween(0m, NegotiationRules.MaxAmount).When(i => i.CounterofferAmount is not null).WithMessage(Money);
            r.RuleFor(i => i.DateSubmitted).GreaterThanOrEqualTo(i => i.DateReceived!.Value)
                .When(i => i.DateReceived is not null && i.DateSubmitted is not null).WithMessage("A counteroffer cannot be submitted before it is received.");
        });
    }
}

public sealed record GetNegotiationQuery(Guid ParcelId) : IRequest<NegotiationDto?>;

public sealed class GetNegotiationQueryHandler : IRequestHandler<GetNegotiationQuery, NegotiationDto?>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public GetNegotiationQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<NegotiationDto?> Handle(GetNegotiationQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelNegotiation, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var n = await NegotiationRules.WithChildren(_db.ParcelNegotiations.AsNoTracking()).FirstOrDefaultAsync(x => x.ParcelId == parcel.Id, ct);
        return n is null ? null : NegotiationRules.ToDto(n, parcel.Number);
    }
}

/// <summary>Saves the parcel's negotiation record: creates it the first time (needs Create) and changes it after that (needs Update).</summary>
public sealed record SaveNegotiationCommand(
    Guid ParcelId, string? AssignedTo, decimal? TotalAgreementAmount, string? Notes, DateOnly? CounterofferSubmittedToJatDate, DateOnly? CounterofferResponseFromJatDate,
    string? JatResponse, bool FtaApprovalRequiredForCounteroffer, DateOnly? PuaRequestedDate, DateOnly? PuaApprovedDate, decimal? PuaApprovedAmount,
    DateOnly? PuaExecutedDate, DateOnly? PuaClosedDate, IReadOnlyList<NegotiationNoteInput>? StatusNotes,
    IReadOnlyList<NegotiationOfferInput>? Offers, IReadOnlyList<NegotiationSettlementInput>? Settlements)
    : IRequest<NegotiationDto>, INegotiationFields;

public sealed class SaveNegotiationCommandValidator : NegotiationFieldsValidator<SaveNegotiationCommand>
{
    public SaveNegotiationCommandValidator() => RuleFor(x => x.ParcelId).NotEmpty();
}

public sealed class SaveNegotiationCommandHandler : IRequestHandler<SaveNegotiationCommand, NegotiationDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public SaveNegotiationCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<NegotiationDto> Handle(SaveNegotiationCommand request, CancellationToken ct)
    {
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var existing = await NegotiationRules.WithChildren(_db.ParcelNegotiations).FirstOrDefaultAsync(x => x.ParcelId == parcel.Id, ct);
        await _permissions.EnsureAsync(PageKeys.ParcelNegotiation, existing is null ? PermissionAction.Create : PermissionAction.Update, ct);

        var children = NegotiationRules.Build(request);
        ParcelNegotiation record;
        if (existing is null)
        {
            record = new ParcelNegotiation { ParcelId = parcel.Id, Number = $"NEG-{parcel.Number}" };
            NegotiationRules.Apply(record, request);
            _db.ParcelNegotiations.Add(record);
            NegotiationRules.Attach(_db, record, children);
        }
        else
        {
            record = existing;
            NegotiationRules.Apply(record, request);
            NegotiationRules.RemoveChildren(_db, record);
            NegotiationRules.Attach(_db, record, children);
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // Two people saved the first version at once; the other one won.
            _db.ChangeTracker.Clear();
            throw new ConflictException("negotiation_exists", "Someone saved this parcel's negotiation record at the same moment. Reload it and try again.");
        }

        var fresh = await NegotiationRules.WithChildren(_db.ParcelNegotiations.AsNoTracking()).FirstAsync(x => x.Id == record.Id, ct);
        return NegotiationRules.ToDto(fresh, parcel.Number);
    }
}

public sealed record RemoveNegotiationCommand(Guid ParcelId) : IRequest;

public sealed class RemoveNegotiationCommandHandler : IRequestHandler<RemoveNegotiationCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveNegotiationCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveNegotiationCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelNegotiation, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var n = await NegotiationRules.WithChildren(_db.ParcelNegotiations).FirstOrDefaultAsync(x => x.ParcelId == parcel.Id, ct)
                ?? throw new NotFoundException("Negotiation record", parcel.Id);
        NegotiationRules.RemoveChildren(_db, n);
        _db.ParcelNegotiations.Remove(n);
        await _db.SaveChangesAsync(ct);
    }
}
