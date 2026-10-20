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

// ── Condemnation (Phase 1.5, ADR 0020) ──────────────────────────────────

public sealed record CondemnationAttorneyInput(string PartyRepresented, string? AttorneyName, string? Firm, string? Address, string? Phone, string? Email);
public sealed record CondemnationCommissionerInput(DateOnly? AppointmentDate, string? AppointmentType, string Name, string? Address, string? Email, string? Phone, bool Struck);
public sealed record CondemnationNoticeInput(string PartyToServe, DateOnly? ServiceSubmitted, DateOnly? ServiceCompleted, DateOnly? NohFiled, string? Comments);

public sealed record CondemnationAttorneyDto(Guid Id, string PartyRepresented, string? AttorneyName, string? Firm, string? Address, string? Phone, string? Email);
public sealed record CondemnationCommissionerDto(Guid Id, DateOnly? AppointmentDate, string? AppointmentType, string Name, string? Address, string? Email, string? Phone, bool Struck);
public sealed record CondemnationNoticeDto(Guid Id, string PartyToServe, DateOnly? ServiceSubmitted, DateOnly? ServiceCompleted, DateOnly? NohFiled, string? Comments);

/// <summary>The dates, people and flags of the condemnation. <see cref="VestedOwnerName"/> is read-only: the parcel's ownerships of type "Vested Owner".</summary>
public sealed record CondemnationDetails(
    DateOnly? PrelimPackageSubmittedToAtp, DateOnly? PrelimPackageApprovedByAtp, DateOnly? PrelimConcurrenceMemoSentToFta, DateOnly? JatApprovalOfPrelimPackage,
    DateOnly? JatApprovedPackageToCityLegal, DateOnly? CityLegalReviewCompleted, DateOnly? PrelimPackageSubmittedForCouncil, DateOnly? CityCouncilApproval,
    string? EdCoordinator, string? OutsideCounsel, string? AppraisalWitness, string? EngineeringWitness, string? ExpertWitness1, string? ExpertWitness2,
    DateOnly? PetitionPackageReceived, DateOnly? PetitionPackageReviewedByAtp, string? ProbateCauseNo, DateOnly? PetitionFiledDate, DateOnly? LisPendensRecordedDate,
    DateOnly? LisPendensLettersMailedDate, DateOnly? CommissionerStrikePeriodExpires, DateOnly? CommissionerNotificationLettersMailed, DateOnly? OrderSettingHearingDate,
    DateOnly? OathsOfCommissionersDate, DateOnly? PrehearingScheduled, DateOnly? CommissionerFeesDeposited, DateOnly? ObjectionDeadline, bool ObjectionsFiled);

public sealed record CondemnationDto(
    Guid Id, Guid ParcelId, string Number, string ParcelNumber, string? VestedOwnerName, CondemnationDetails Details,
    IReadOnlyList<StatusNoteDto> StatusNotes, IReadOnlyList<CondemnationAttorneyDto> Attorneys, IReadOnlyList<CondemnationCommissionerDto> Commissioners,
    IReadOnlyList<CondemnationNoticeDto> Notices, DateTime UpdatedAt);

internal static class CondemnationRules
{
    public static IQueryable<ParcelCondemnation> WithChildren(IQueryable<ParcelCondemnation> q) => q.Include(x => x.Attorneys).Include(x => x.Commissioners).Include(x => x.Notices);

    public static CondemnationDetails DetailsOf(ParcelCondemnation c) => new(
        c.PrelimPackageSubmittedToAtp, c.PrelimPackageApprovedByAtp, c.PrelimConcurrenceMemoSentToFta, c.JatApprovalOfPrelimPackage, c.JatApprovedPackageToCityLegal,
        c.CityLegalReviewCompleted, c.PrelimPackageSubmittedForCouncil, c.CityCouncilApproval, c.EdCoordinator, c.OutsideCounsel, c.AppraisalWitness, c.EngineeringWitness,
        c.ExpertWitness1, c.ExpertWitness2, c.PetitionPackageReceived, c.PetitionPackageReviewedByAtp, c.ProbateCauseNo, c.PetitionFiledDate, c.LisPendensRecordedDate,
        c.LisPendensLettersMailedDate, c.CommissionerStrikePeriodExpires, c.CommissionerNotificationLettersMailed, c.OrderSettingHearingDate, c.OathsOfCommissionersDate,
        c.PrehearingScheduled, c.CommissionerFeesDeposited, c.ObjectionDeadline, c.ObjectionsFiled);

    public static async Task<CondemnationDto> ToDtoAsync(IRowDbContext db, ParcelCondemnation c, string parcelNumber, CancellationToken ct) =>
        new(c.Id, c.ParcelId, c.Number, parcelNumber, await TitleClosingRules.VestedOwnerNameAsync(db, c.ParcelId, ct), DetailsOf(c),
            await StatusNoteRules.LoadAsync(db, StatusNoteKinds.Condemnation, c.Id, ct),
            c.Attorneys.OrderBy(x => x.CreatedAt).Select(x => new CondemnationAttorneyDto(x.Id, x.PartyRepresented, x.AttorneyName, x.Firm, x.Address, x.Phone, x.Email)).ToList(),
            c.Commissioners.OrderBy(x => x.CreatedAt).Select(x => new CondemnationCommissionerDto(x.Id, x.AppointmentDate, x.AppointmentType, x.Name, x.Address, x.Email, x.Phone, x.Struck)).ToList(),
            c.Notices.OrderBy(x => x.CreatedAt).Select(x => new CondemnationNoticeDto(x.Id, x.PartyToServe, x.ServiceSubmitted, x.ServiceCompleted, x.NohFiled, x.Comments)).ToList(), c.UpdatedAt);

    public static void Apply(ParcelCondemnation c, CondemnationDetails d)
    {
        c.PrelimPackageSubmittedToAtp = d.PrelimPackageSubmittedToAtp; c.PrelimPackageApprovedByAtp = d.PrelimPackageApprovedByAtp;
        c.PrelimConcurrenceMemoSentToFta = d.PrelimConcurrenceMemoSentToFta; c.JatApprovalOfPrelimPackage = d.JatApprovalOfPrelimPackage;
        c.JatApprovedPackageToCityLegal = d.JatApprovedPackageToCityLegal; c.CityLegalReviewCompleted = d.CityLegalReviewCompleted;
        c.PrelimPackageSubmittedForCouncil = d.PrelimPackageSubmittedForCouncil; c.CityCouncilApproval = d.CityCouncilApproval;
        c.EdCoordinator = ParcelRecordHelpers.Clean(d.EdCoordinator); c.OutsideCounsel = ParcelRecordHelpers.Clean(d.OutsideCounsel);
        c.AppraisalWitness = ParcelRecordHelpers.Clean(d.AppraisalWitness); c.EngineeringWitness = ParcelRecordHelpers.Clean(d.EngineeringWitness);
        c.ExpertWitness1 = ParcelRecordHelpers.Clean(d.ExpertWitness1); c.ExpertWitness2 = ParcelRecordHelpers.Clean(d.ExpertWitness2);
        c.PetitionPackageReceived = d.PetitionPackageReceived; c.PetitionPackageReviewedByAtp = d.PetitionPackageReviewedByAtp;
        c.ProbateCauseNo = ParcelRecordHelpers.Clean(d.ProbateCauseNo); c.PetitionFiledDate = d.PetitionFiledDate;
        c.LisPendensRecordedDate = d.LisPendensRecordedDate; c.LisPendensLettersMailedDate = d.LisPendensLettersMailedDate;
        c.CommissionerStrikePeriodExpires = d.CommissionerStrikePeriodExpires; c.CommissionerNotificationLettersMailed = d.CommissionerNotificationLettersMailed;
        c.OrderSettingHearingDate = d.OrderSettingHearingDate; c.OathsOfCommissionersDate = d.OathsOfCommissionersDate; c.PrehearingScheduled = d.PrehearingScheduled;
        c.CommissionerFeesDeposited = d.CommissionerFeesDeposited; c.ObjectionDeadline = d.ObjectionDeadline; c.ObjectionsFiled = d.ObjectionsFiled;
    }

    public static (List<CondemnationAttorney>, List<CondemnationCommissioner>, List<CondemnationNotice>) Build(
        IReadOnlyList<CondemnationAttorneyInput>? attorneys, IReadOnlyList<CondemnationCommissionerInput>? commissioners, IReadOnlyList<CondemnationNoticeInput>? notices) => (
        (attorneys ?? Array.Empty<CondemnationAttorneyInput>()).Select(a => new CondemnationAttorney
        {
            PartyRepresented = a.PartyRepresented.Trim(), AttorneyName = ParcelRecordHelpers.Clean(a.AttorneyName), Firm = ParcelRecordHelpers.Clean(a.Firm),
            Address = ParcelRecordHelpers.Clean(a.Address), Phone = ParcelRecordHelpers.Clean(a.Phone), Email = ParcelRecordHelpers.Clean(a.Email),
        }).ToList(),
        (commissioners ?? Array.Empty<CondemnationCommissionerInput>()).Select(c => new CondemnationCommissioner
        {
            AppointmentDate = c.AppointmentDate, AppointmentType = ParcelRecordHelpers.Clean(c.AppointmentType), Name = c.Name.Trim(), Address = ParcelRecordHelpers.Clean(c.Address),
            Email = ParcelRecordHelpers.Clean(c.Email), Phone = ParcelRecordHelpers.Clean(c.Phone), Struck = c.Struck,
        }).ToList(),
        (notices ?? Array.Empty<CondemnationNoticeInput>()).Select(n => new CondemnationNotice
        {
            PartyToServe = n.PartyToServe.Trim(), ServiceSubmitted = n.ServiceSubmitted, ServiceCompleted = n.ServiceCompleted, NohFiled = n.NohFiled, Comments = ParcelRecordHelpers.Clean(n.Comments),
        }).ToList());
}

public sealed class CondemnationFieldsValidator<T> : AbstractValidator<T> where T : ICondemnationFields
{
    public CondemnationFieldsValidator()
    {
        When(x => x.Details is not null, () =>
        {
            RuleFor(x => x.Details.EdCoordinator).MaximumLength(200);
            RuleFor(x => x.Details.OutsideCounsel).MaximumLength(200);
            RuleFor(x => x.Details.AppraisalWitness).MaximumLength(200);
            RuleFor(x => x.Details.EngineeringWitness).MaximumLength(200);
            RuleFor(x => x.Details.ExpertWitness1).MaximumLength(200);
            RuleFor(x => x.Details.ExpertWitness2).MaximumLength(200);
            RuleFor(x => x.Details.ProbateCauseNo).MaximumLength(100);
            RuleFor(x => x.Details.PrelimPackageApprovedByAtp).GreaterThanOrEqualTo(x => x.Details.PrelimPackageSubmittedToAtp!.Value)
                .When(x => x.Details.PrelimPackageSubmittedToAtp is not null && x.Details.PrelimPackageApprovedByAtp is not null).WithMessage("The package cannot be approved before it is submitted.");
            RuleFor(x => x.Details.PetitionPackageReviewedByAtp).GreaterThanOrEqualTo(x => x.Details.PetitionPackageReceived!.Value)
                .When(x => x.Details.PetitionPackageReceived is not null && x.Details.PetitionPackageReviewedByAtp is not null).WithMessage("The petition package cannot be reviewed before it is received.");
            RuleFor(x => x.Details.ObjectionDeadline).GreaterThanOrEqualTo(x => x.Details.PetitionFiledDate!.Value)
                .When(x => x.Details.PetitionFiledDate is not null && x.Details.ObjectionDeadline is not null).WithMessage("The objection deadline cannot be before the petition is filed.");
        });
        StatusNoteRules.AddRules(this, x => x.StatusNotes);
        RuleFor(x => x.Attorneys).Must(r => r is null || r.Count <= 50).WithMessage("At most 50 attorneys of record.");
        RuleForEach(x => x.Attorneys).ChildRules(r =>
        {
            r.RuleFor(i => i.PartyRepresented).NotEmpty().WithMessage("Each attorney needs the party represented.").MaximumLength(200);
            r.RuleFor(i => i.AttorneyName).MaximumLength(200);
            r.RuleFor(i => i.Firm).MaximumLength(200);
            r.RuleFor(i => i.Address).MaximumLength(500);
            r.RuleFor(i => i.Phone).MaximumLength(100);
            r.RuleFor(i => i.Email).MaximumLength(200);
        });
        RuleFor(x => x.Commissioners).Must(r => r is null || r.Count <= 50).WithMessage("At most 50 special commissioners.");
        RuleForEach(x => x.Commissioners).ChildRules(r =>
        {
            r.RuleFor(i => i.Name).NotEmpty().WithMessage("Each commissioner needs a name.").MaximumLength(200);
            r.RuleFor(i => i.AppointmentType).MaximumLength(100);
            r.RuleFor(i => i.Address).MaximumLength(500);
            r.RuleFor(i => i.Email).MaximumLength(200);
            r.RuleFor(i => i.Phone).MaximumLength(100);
        });
        RuleFor(x => x.Notices).Must(r => r is null || r.Count <= 50).WithMessage("At most 50 notices of hearing.");
        RuleForEach(x => x.Notices).ChildRules(r =>
        {
            r.RuleFor(i => i.PartyToServe).NotEmpty().WithMessage("Each notice needs the party to serve.").MaximumLength(200);
            r.RuleFor(i => i.Comments).MaximumLength(2000);
            r.RuleFor(i => i.ServiceCompleted).GreaterThanOrEqualTo(i => i.ServiceSubmitted!.Value)
                .When(i => i.ServiceSubmitted is not null && i.ServiceCompleted is not null).WithMessage("Service cannot be completed before it is submitted.");
        });
    }
}

public interface ICondemnationFields
{
    CondemnationDetails Details { get; }
    IReadOnlyList<StatusNoteInput>? StatusNotes { get; }
    IReadOnlyList<CondemnationAttorneyInput>? Attorneys { get; }
    IReadOnlyList<CondemnationCommissionerInput>? Commissioners { get; }
    IReadOnlyList<CondemnationNoticeInput>? Notices { get; }
}

public sealed record GetCondemnationQuery(Guid ParcelId) : IRequest<CondemnationDto?>;

public sealed class GetCondemnationQueryHandler : IRequestHandler<GetCondemnationQuery, CondemnationDto?>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public GetCondemnationQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<CondemnationDto?> Handle(GetCondemnationQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelCondemnation, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var c = await CondemnationRules.WithChildren(_db.ParcelCondemnations.AsNoTracking()).FirstOrDefaultAsync(x => x.ParcelId == parcel.Id, ct);
        return c is null ? null : await CondemnationRules.ToDtoAsync(_db, c, parcel.Number, ct);
    }
}

/// <summary>Saves the parcel's condemnation record: creates it the first time (needs Create) and changes it after that (needs Update).</summary>
public sealed record SaveCondemnationCommand(
    Guid ParcelId, CondemnationDetails Details, IReadOnlyList<StatusNoteInput>? StatusNotes, IReadOnlyList<CondemnationAttorneyInput>? Attorneys,
    IReadOnlyList<CondemnationCommissionerInput>? Commissioners, IReadOnlyList<CondemnationNoticeInput>? Notices) : IRequest<CondemnationDto>, ICondemnationFields;

public sealed class SaveCondemnationCommandValidator : AbstractValidator<SaveCondemnationCommand>
{
    public SaveCondemnationCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.Details).NotNull().WithMessage("The condemnation details are required.");
        Include(new CondemnationFieldsValidator<SaveCondemnationCommand>());
    }
}

public sealed class SaveCondemnationCommandHandler : IRequestHandler<SaveCondemnationCommand, CondemnationDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public SaveCondemnationCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<CondemnationDto> Handle(SaveCondemnationCommand request, CancellationToken ct)
    {
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var existing = await CondemnationRules.WithChildren(_db.ParcelCondemnations).FirstOrDefaultAsync(x => x.ParcelId == parcel.Id, ct);
        await _permissions.EnsureAsync(PageKeys.ParcelCondemnation, existing is null ? PermissionAction.Create : PermissionAction.Update, ct);

        var (attorneys, commissioners, notices) = CondemnationRules.Build(request.Attorneys, request.Commissioners, request.Notices);
        var record = existing ?? new ParcelCondemnation { ParcelId = parcel.Id, Number = $"CON-{parcel.Number}" };
        CondemnationRules.Apply(record, request.Details);
        if (existing is null)
        {
            record.Attorneys = attorneys; record.Commissioners = commissioners; record.Notices = notices;
            _db.ParcelCondemnations.Add(record);
        }
        else
        {
            _db.CondemnationAttorneys.RemoveRange(record.Attorneys);
            _db.CondemnationCommissioners.RemoveRange(record.Commissioners);
            _db.CondemnationNotices.RemoveRange(record.Notices);
            foreach (var a in attorneys) a.CondemnationId = record.Id;
            foreach (var c in commissioners) c.CondemnationId = record.Id;
            foreach (var n in notices) n.CondemnationId = record.Id;
            _db.CondemnationAttorneys.AddRange(attorneys);
            _db.CondemnationCommissioners.AddRange(commissioners);
            _db.CondemnationNotices.AddRange(notices);
        }
        await StatusNoteRules.ReplaceAsync(_db, StatusNoteKinds.Condemnation, record.Id, request.StatusNotes, ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (existing is null)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("condemnation_exists", "Someone saved this parcel's condemnation record at the same moment. Reload it and try again.");
        }

        var fresh = await CondemnationRules.WithChildren(_db.ParcelCondemnations.AsNoTracking()).FirstAsync(x => x.Id == record.Id, ct);
        return await CondemnationRules.ToDtoAsync(_db, fresh, parcel.Number, ct);
    }
}

public sealed record RemoveCondemnationCommand(Guid ParcelId) : IRequest;

public sealed class RemoveCondemnationCommandHandler : IRequestHandler<RemoveCondemnationCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveCondemnationCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveCondemnationCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelCondemnation, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var c = await CondemnationRules.WithChildren(_db.ParcelCondemnations).FirstOrDefaultAsync(x => x.ParcelId == parcel.Id, ct)
                ?? throw new NotFoundException("Condemnation record", parcel.Id);
        _db.CondemnationAttorneys.RemoveRange(c.Attorneys);
        _db.CondemnationCommissioners.RemoveRange(c.Commissioners);
        _db.CondemnationNotices.RemoveRange(c.Notices);
        await StatusNoteRules.DeleteAsync(_db, StatusNoteKinds.Condemnation, c.Id, ct);
        _db.ParcelCondemnations.Remove(c);
        await _db.SaveChangesAsync(ct);
    }
}
