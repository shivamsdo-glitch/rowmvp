using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
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

// ── Relocation (Phase 1.5, ADR 0020) ────────────────────────────────────

public sealed record RelocationAgentInput(string AgentName);
public sealed record RelocationContactInput(string Name, string? ContactType, string? Contact, string? Email, string? Address, bool IsPrimary);
public sealed record RelocationEligibilityInput(string EligibilityType, decimal? ClaimLimit, DateOnly? SubmittedForApproval, DateOnly? Approved);
public sealed record RelocationContactLogInput(DateTime ContactedAt, string? Mode, string? ContactedBy, string? ContactPersons, string? Notes);

public sealed record RelocationAgentDto(Guid Id, string AgentName, string? AgentType, string? Company, string? Phone, string? Email);
public sealed record RelocationContactDto(Guid Id, string Name, string? ContactType, string? Contact, string? Email, string? Address, bool IsPrimary);
public sealed record RelocationEligibilityDto(Guid Id, string EligibilityType, decimal? ClaimLimit, DateOnly? SubmittedForApproval, DateOnly? Approved);
public sealed record RelocationContactLogDto(Guid Id, DateTime ContactedAt, string? Mode, string? ContactedBy, string? ContactPersons, string? Notes);

public sealed record RelocationDetails(
    string RelocationNumber, string? AssignedTo, string DisplaceeName, bool LimitedEnglishProficiency, string? DisplaceeStatus, string? DisplaceeInterest,
    string? DisplaceeMailingAddress, string RelocationType, string? PropertySiteAddress,
    DateOnly? GinMailedDate, DateOnly? GinDeliveredDate, DateOnly? InitialInterview, DateOnly? CertificateOfEligibility, DateOnly? NoticeOfEligibility,
    DateOnly? DeliveryOfNoticeOfEligibility, DateOnly? Notice90Day, DateOnly? DeliveryOf90DayNotice, DateOnly? VacateEligibleDate, DateOnly? Notice30DayVacate,
    DateOnly? Notice30DayVacateDelivered, bool DemolitionRequired, DateOnly? TargetVacateDate, DateOnly? ActualVacateDate, bool IneligibleForRelocation,
    DateOnly? MovePlan, DateOnly? CertificationOfInventory, DateOnly? BenefitsExpirationLetterDate, DateOnly? BenefitExpiration, DateOnly? ReleaseOfProperty,
    DateOnly? CertificateOfCompletion, DateOnly? FileCloseOutSubmitted, DateOnly? RelocationComplete, DateOnly? RelocationComplete1,
    Guid? PartyLinkId = null, DateOnly? PlanningStudyDate = null, string? AdvisoryNeeds = null, DateOnly? ReplacementHousingStudyDate = null,
    DateOnly? ComparableNoticeDate = null, DateOnly? InspectionDate = null, bool? DecentSafeSanitary = null, bool HousingOfLastResort = false,
    string? EligibilityException = null, DateOnly? ClaimDeniedDate = null, DateOnly? AppealFiledDate = null, DateOnly? AppealDecisionDate = null,
    string? AppealOutcome = null);

public sealed record RelocationPaymentApprovalDto(string Role, DateTime ApprovedAt);
public sealed record RelocationPaymentDto(
    Guid Id, string PaymentType, decimal Amount, bool IsAdvance, bool Taxable, string? Notes, string Status, IReadOnlyList<string> RequiredRoles,
    IReadOnlyList<RelocationPaymentApprovalDto> Approvals, string? NextApprover, string? DecisionNote, DateTime? PaidAt, string? PaymentReference,
    DateTime RequestedAt, bool RequestedByMe);

/// <summary><see cref="VestedOwnerName"/> and each agent's type, company, phone and email are read-only.</summary>
public sealed record RelocationDto(
    Guid Id, Guid ParcelId, string ParcelNumber, string? VestedOwnerName, RelocationDetails Details, IReadOnlyList<StatusNoteDto> StatusNotes,
    IReadOnlyList<RelocationAgentDto> Agents, IReadOnlyList<RelocationContactDto> Contacts, IReadOnlyList<RelocationEligibilityDto> Eligibilities,
    IReadOnlyList<RelocationContactLogDto> ContactLog, DateTime UpdatedAt,
    IReadOnlyList<RelocationPaymentDto> Payments, string? InterestOwnerName, DateOnly? ClaimDeadline, string Stage);

internal static class RelocationRules
{
    public const decimal MaxAmount = 1_000_000_000_000m;

    public static IQueryable<ParcelRelocation> WithChildren(IQueryable<ParcelRelocation> q) => q.Include(x => x.Agents).Include(x => x.Contacts).Include(x => x.Eligibilities).Include(x => x.ContactLog).Include(x => x.Payments).ThenInclude(p => p.Approvals);

    public static RelocationDetails DetailsOf(ParcelRelocation r) => new(
        r.RelocationNumber, r.AssignedTo, r.DisplaceeName, r.LimitedEnglishProficiency, r.DisplaceeStatus, r.DisplaceeInterest, r.DisplaceeMailingAddress, r.RelocationType,
        r.PropertySiteAddress, r.GinMailedDate, r.GinDeliveredDate, r.InitialInterview, r.CertificateOfEligibility, r.NoticeOfEligibility, r.DeliveryOfNoticeOfEligibility,
        r.Notice90Day, r.DeliveryOf90DayNotice, r.VacateEligibleDate, r.Notice30DayVacate, r.Notice30DayVacateDelivered, r.DemolitionRequired, r.TargetVacateDate,
        r.ActualVacateDate, r.IneligibleForRelocation, r.MovePlan, r.CertificationOfInventory, r.BenefitsExpirationLetterDate, r.BenefitExpiration, r.ReleaseOfProperty,
        r.CertificateOfCompletion, r.FileCloseOutSubmitted, r.RelocationComplete, r.RelocationComplete1,
        r.PartyLinkId, r.PlanningStudyDate, r.AdvisoryNeeds, r.ReplacementHousingStudyDate, r.ComparableNoticeDate, r.InspectionDate, r.DecentSafeSanitary,
        r.HousingOfLastResort, r.EligibilityException, r.ClaimDeniedDate, r.AppealFiledDate, r.AppealDecisionDate, r.AppealOutcome);

    public static RelocationPaymentDto PaymentDto(RelocationPayment p, Guid me)
    {
        var required = RelocationCalc.ParseRoles(p.RequiredRoles);
        var signed = p.Approvals.OrderBy(a => a.ApprovedAt).ToList();
        var next = p.Status == RelocationPaymentStatus.Requested ? ApprovalChain.NextApprover(new ApprovalRequirement(new ApprovalBand(0, null, required), required), signed.Select(a => a.Role).ToList()) : null;
        return new(p.Id, p.PaymentType, p.Amount, p.IsAdvance, p.Taxable, p.Notes, p.Status.ToString(), required,
            signed.Select(a => new RelocationPaymentApprovalDto(a.Role, a.ApprovedAt)).ToList(), next, p.DecisionNote, p.PaidAt, p.PaymentReference, p.CreatedAt, p.CreatedBy == me);
    }

    public static async Task<RelocationDto> ToDtoAsync(IRowDbContext db, ParcelRelocation r, string parcelNumber, Guid me, CancellationToken ct)
    {
        string? interestOwner = null;
        if (r.PartyLinkId is { } linkId)
            interestOwner = await (from l in db.PartyLinks.AsNoTracking() join pa in db.Parties.AsNoTracking() on l.PartyId equals pa.Id where l.Id == linkId select pa.Name).FirstOrDefaultAsync(ct);
        var library = await db.LibraryAgents.AsNoTracking().ToListAsync(ct);
        LibraryAgent? Find(string name) => library.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        return new(r.Id, r.ParcelId, parcelNumber, await TitleClosingRules.VestedOwnerNameAsync(db, r.ParcelId, ct), DetailsOf(r),
            await StatusNoteRules.LoadAsync(db, StatusNoteKinds.Relocation, r.Id, ct),
            r.Agents.OrderBy(a => a.CreatedAt).Select(a => { var l = Find(a.AgentName); return new RelocationAgentDto(a.Id, a.AgentName, l?.AgentType, l?.Company, l?.Phone, l?.Email); }).ToList(),
            r.Contacts.OrderBy(c => c.CreatedAt).Select(c => new RelocationContactDto(c.Id, c.Name, c.ContactType, c.Contact, c.Email, c.Address, c.IsPrimary)).ToList(),
            r.Eligibilities.OrderBy(e => e.CreatedAt).Select(e => new RelocationEligibilityDto(e.Id, e.EligibilityType, e.ClaimLimit, e.SubmittedForApproval, e.Approved)).ToList(),
            r.ContactLog.OrderByDescending(c => c.ContactedAt).Select(c => new RelocationContactLogDto(c.Id, c.ContactedAt, c.Mode, c.ContactedBy, c.ContactPersons, c.Notes)).ToList(), r.UpdatedAt,
            r.Payments.OrderBy(p => p.CreatedAt).Select(p => PaymentDto(p, me)).ToList(), interestOwner, RelocationCalc.ClaimDeadline(r.ActualVacateDate),
            RelocationCalc.Stage(r.GinMailedDate, r.InitialInterview, r.NoticeOfEligibility, r.ReplacementHousingStudyDate, r.Notice90Day, r.Notice30DayVacate,
                r.ActualVacateDate, r.RelocationComplete, r.IneligibleForRelocation));
    }

    public static void Apply(ParcelRelocation r, RelocationDetails d)
    {
        r.RelocationNumber = d.RelocationNumber.Trim(); r.AssignedTo = ParcelRecordHelpers.Clean(d.AssignedTo); r.DisplaceeName = d.DisplaceeName.Trim();
        r.LimitedEnglishProficiency = d.LimitedEnglishProficiency; r.DisplaceeStatus = ParcelRecordHelpers.Clean(d.DisplaceeStatus);
        r.DisplaceeInterest = ParcelRecordHelpers.Clean(d.DisplaceeInterest); r.DisplaceeMailingAddress = ParcelRecordHelpers.Clean(d.DisplaceeMailingAddress);
        r.RelocationType = d.RelocationType.Trim(); r.PropertySiteAddress = ParcelRecordHelpers.Clean(d.PropertySiteAddress);
        r.GinMailedDate = d.GinMailedDate; r.GinDeliveredDate = d.GinDeliveredDate; r.InitialInterview = d.InitialInterview; r.CertificateOfEligibility = d.CertificateOfEligibility;
        r.NoticeOfEligibility = d.NoticeOfEligibility; r.DeliveryOfNoticeOfEligibility = d.DeliveryOfNoticeOfEligibility; r.Notice90Day = d.Notice90Day;
        r.DeliveryOf90DayNotice = d.DeliveryOf90DayNotice; r.VacateEligibleDate = d.VacateEligibleDate; r.Notice30DayVacate = d.Notice30DayVacate;
        r.Notice30DayVacateDelivered = d.Notice30DayVacateDelivered; r.DemolitionRequired = d.DemolitionRequired; r.TargetVacateDate = d.TargetVacateDate;
        r.ActualVacateDate = d.ActualVacateDate; r.IneligibleForRelocation = d.IneligibleForRelocation; r.MovePlan = d.MovePlan;
        r.CertificationOfInventory = d.CertificationOfInventory; r.BenefitsExpirationLetterDate = d.BenefitsExpirationLetterDate; r.BenefitExpiration = d.BenefitExpiration;
        r.ReleaseOfProperty = d.ReleaseOfProperty; r.CertificateOfCompletion = d.CertificateOfCompletion; r.FileCloseOutSubmitted = d.FileCloseOutSubmitted;
        r.RelocationComplete = d.RelocationComplete; r.RelocationComplete1 = d.RelocationComplete1;
        r.PartyLinkId = d.PartyLinkId; r.PlanningStudyDate = d.PlanningStudyDate; r.AdvisoryNeeds = ParcelRecordHelpers.Clean(d.AdvisoryNeeds);
        r.ReplacementHousingStudyDate = d.ReplacementHousingStudyDate; r.ComparableNoticeDate = d.ComparableNoticeDate; r.InspectionDate = d.InspectionDate;
        r.DecentSafeSanitary = d.DecentSafeSanitary; r.HousingOfLastResort = d.HousingOfLastResort; r.EligibilityException = ParcelRecordHelpers.Clean(d.EligibilityException);
        r.ClaimDeniedDate = d.ClaimDeniedDate; r.AppealFiledDate = d.AppealFiledDate; r.AppealDecisionDate = d.AppealDecisionDate;
        r.AppealOutcome = ParcelRecordHelpers.Clean(d.AppealOutcome);
    }

    /// <summary>The interest a relocation is for must be an ownership interest on this parcel (its own or its tract's).</summary>
    public static async Task EnsureInterestAsync(IRowDbContext db, Parcel parcel, Guid? linkId, CancellationToken ct)
    {
        if (linkId is not { } id) return;
        var link = await db.PartyLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id && (l.ParcelId == parcel.Id || (parcel.ParentTractId != null && l.ParentTractId == parcel.ParentTractId)), ct);
        if (link is null || PartyRoles.CategoryOf(link.Role) != PartyCategory.Ownership)
            throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("PartyLinkId", "Choose an ownership interest of this parcel.") });
    }

    public static DateTime Utc(DateTime t) => t.Kind == DateTimeKind.Utc ? t : t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : DateTime.SpecifyKind(t, DateTimeKind.Utc);

    public sealed record Children(List<RelocationAgentRow> Agents, List<RelocationContact> Contacts, List<RelocationEligibility> Eligibilities, List<RelocationContactLogRow> ContactLog);

    public static Children Build(IRelocationFields f) => new(
        (f.Agents ?? Array.Empty<RelocationAgentInput>()).Select(a => new RelocationAgentRow { AgentName = a.AgentName.Trim() }).ToList(),
        (f.Contacts ?? Array.Empty<RelocationContactInput>()).Select(c => new RelocationContact
        {
            Name = c.Name.Trim(), ContactType = ParcelRecordHelpers.Clean(c.ContactType), Contact = ParcelRecordHelpers.Clean(c.Contact), Email = ParcelRecordHelpers.Clean(c.Email),
            Address = ParcelRecordHelpers.Clean(c.Address), IsPrimary = c.IsPrimary,
        }).ToList(),
        (f.Eligibilities ?? Array.Empty<RelocationEligibilityInput>()).Select(e => new RelocationEligibility
        {
            EligibilityType = e.EligibilityType.Trim(), ClaimLimit = e.ClaimLimit, SubmittedForApproval = e.SubmittedForApproval, Approved = e.Approved,
        }).ToList(),
        (f.ContactLog ?? Array.Empty<RelocationContactLogInput>()).Select(c => new RelocationContactLogRow
        {
            ContactedAt = Utc(c.ContactedAt), Mode = ParcelRecordHelpers.Clean(c.Mode), ContactedBy = ParcelRecordHelpers.Clean(c.ContactedBy),
            ContactPersons = ParcelRecordHelpers.Clean(c.ContactPersons), Notes = ParcelRecordHelpers.Clean(c.Notes),
        }).ToList());

    public static void Attach(IRowDbContext db, Guid id, Children c)
    {
        foreach (var x in c.Agents) x.RelocationId = id;
        foreach (var x in c.Contacts) x.RelocationId = id;
        foreach (var x in c.Eligibilities) x.RelocationId = id;
        foreach (var x in c.ContactLog) x.RelocationId = id;
        db.RelocationAgents.AddRange(c.Agents);
        db.RelocationContacts.AddRange(c.Contacts);
        db.RelocationEligibilities.AddRange(c.Eligibilities);
        db.RelocationContactLogRows.AddRange(c.ContactLog);
    }

    public static void RemoveChildren(IRowDbContext db, ParcelRelocation r)
    {
        db.RelocationAgents.RemoveRange(r.Agents);
        db.RelocationContacts.RemoveRange(r.Contacts);
        db.RelocationEligibilities.RemoveRange(r.Eligibilities);
        db.RelocationContactLogRows.RemoveRange(r.ContactLog);
    }
}

public interface IRelocationFields
{
    RelocationDetails Details { get; }
    IReadOnlyList<StatusNoteInput>? StatusNotes { get; }
    IReadOnlyList<RelocationAgentInput>? Agents { get; }
    IReadOnlyList<RelocationContactInput>? Contacts { get; }
    IReadOnlyList<RelocationEligibilityInput>? Eligibilities { get; }
    IReadOnlyList<RelocationContactLogInput>? ContactLog { get; }
}

public abstract class RelocationFieldsValidator<T> : AbstractValidator<T> where T : IRelocationFields
{
    protected RelocationFieldsValidator()
    {
        RuleFor(x => x.Details).NotNull().WithMessage("The relocation details are required.");
        When(x => x.Details is not null, () =>
        {
            RuleFor(x => x.Details.RelocationNumber).NotEmpty().WithMessage("Relocation number is required.").MaximumLength(100);
            RuleFor(x => x.Details.DisplaceeName).NotEmpty().WithMessage("Displacee name is required.").MaximumLength(200);
            RuleFor(x => x.Details.RelocationType).NotEmpty().WithMessage("Relocation type is required.").MaximumLength(100);
            RuleFor(x => x.Details.AssignedTo).MaximumLength(200);
            RuleFor(x => x.Details.DisplaceeStatus).MaximumLength(100);
            RuleFor(x => x.Details.DisplaceeInterest).MaximumLength(100);
            RuleFor(x => x.Details.DisplaceeMailingAddress).MaximumLength(1000);
            RuleFor(x => x.Details.PropertySiteAddress).MaximumLength(1000);
            RuleFor(x => x.Details.GinDeliveredDate).GreaterThanOrEqualTo(x => x.Details.GinMailedDate!.Value)
                .When(x => x.Details.GinMailedDate is not null && x.Details.GinDeliveredDate is not null).WithMessage("The GIN cannot be delivered before it is mailed.");
            RuleFor(x => x.Details.ActualVacateDate).GreaterThanOrEqualTo(x => x.Details.InitialInterview!.Value)
                .When(x => x.Details.InitialInterview is not null && x.Details.ActualVacateDate is not null).WithMessage("The displacee cannot vacate before the initial interview.");
            RuleFor(x => x.Details.Notice30DayVacateDelivered).GreaterThanOrEqualTo(x => x.Details.Notice30DayVacate!.Value)
                .When(x => x.Details.Notice30DayVacate is not null && x.Details.Notice30DayVacateDelivered is not null).WithMessage("The notice cannot be delivered before it is issued.");
            RuleFor(x => x.Details.EligibilityException).Must(e => e is null || RelocationExceptions.All.Contains(e)).WithMessage("Choose an exception from the list.");
            RuleFor(x => x.Details.AdvisoryNeeds).MaximumLength(4000);
            RuleFor(x => x.Details.AppealOutcome).MaximumLength(1000);
            RuleFor(x => x.Details.AppealDecisionDate).GreaterThanOrEqualTo(x => x.Details.AppealFiledDate!.Value)
                .When(x => x.Details.AppealFiledDate is not null && x.Details.AppealDecisionDate is not null).WithMessage("The appeal cannot be decided before it is filed.");
            RuleFor(x => x.Details.AppealFiledDate).GreaterThanOrEqualTo(x => x.Details.ClaimDeniedDate!.Value)
                .When(x => x.Details.ClaimDeniedDate is not null && x.Details.AppealFiledDate is not null).WithMessage("The appeal cannot be filed before the claim is denied.");
            RuleFor(x => x.Details.RelocationComplete).GreaterThanOrEqualTo(x => x.Details.ActualVacateDate!.Value)
                .When(x => x.Details.ActualVacateDate is not null && x.Details.RelocationComplete is not null).WithMessage("The relocation cannot be complete before the displacee vacated.");
        });
        StatusNoteRules.AddRules(this, x => x.StatusNotes);
        RuleFor(x => x.Agents).Must(r => r is null || r.Count <= 50).WithMessage("At most 50 agents.");
        RuleForEach(x => x.Agents).ChildRules(r => r.RuleFor(i => i.AgentName).NotEmpty().WithMessage("Each agent needs a name.").MaximumLength(200));
        RuleFor(x => x.Contacts).Must(r => r is null || r.Count <= 50).WithMessage("At most 50 relocation contacts.");
        RuleFor(x => x.Contacts).Must(r => r is null || r.Count(c => c.IsPrimary) <= 1).WithMessage("Only one contact can be the primary contact.");
        RuleForEach(x => x.Contacts).ChildRules(r =>
        {
            r.RuleFor(i => i.Name).NotEmpty().WithMessage("Each contact needs a name.").MaximumLength(200);
            r.RuleFor(i => i.ContactType).MaximumLength(100);
            r.RuleFor(i => i.Contact).MaximumLength(200);
            r.RuleFor(i => i.Email).MaximumLength(200);
            r.RuleFor(i => i.Address).MaximumLength(500);
        });
        RuleFor(x => x.Eligibilities).Must(r => r is null || r.Count <= 50).WithMessage("At most 50 eligibility rows.");
        RuleForEach(x => x.Eligibilities).ChildRules(r =>
        {
            r.RuleFor(i => i.EligibilityType).NotEmpty().WithMessage("Each eligibility row needs a type.").MaximumLength(100);
            r.RuleFor(i => i.ClaimLimit).InclusiveBetween(0m, RelocationRules.MaxAmount).When(i => i.ClaimLimit is not null).WithMessage("The amount must be zero or more.");
            r.RuleFor(i => i.Approved).GreaterThanOrEqualTo(i => i.SubmittedForApproval!.Value)
                .When(i => i.SubmittedForApproval is not null && i.Approved is not null).WithMessage("Eligibility cannot be approved before it is submitted.");
        });
        RuleFor(x => x.ContactLog).Must(r => r is null || r.Count <= 200).WithMessage("At most 200 contact log rows.");
        RuleForEach(x => x.ContactLog).ChildRules(r =>
        {
            r.RuleFor(i => i.ContactedAt).Must(t => t.Year >= 1990 && t.Year <= 2100).WithMessage("Enter the date and time of the contact.");
            r.RuleFor(i => i.Mode).MaximumLength(100);
            r.RuleFor(i => i.ContactedBy).MaximumLength(200);
            r.RuleFor(i => i.ContactPersons).MaximumLength(500);
            r.RuleFor(i => i.Notes).MaximumLength(4000);
        });
    }
}

public sealed record ListRelocationsQuery(Guid ParcelId) : IRequest<IReadOnlyList<RelocationDto>>;

public sealed class ListRelocationsQueryHandler : IRequestHandler<ListRelocationsQuery, IReadOnlyList<RelocationDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;

    public ListRelocationsQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user)
    {
        _user = user;
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<IReadOnlyList<RelocationDto>> Handle(ListRelocationsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelRelocation, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var rows = await RelocationRules.WithChildren(_db.ParcelRelocations.AsNoTracking()).Where(r => r.ParcelId == parcel.Id).ToListAsync(ct);
        var result = new List<RelocationDto>();
        foreach (var r in rows.OrderBy(r => r.RelocationNumber, StringComparer.OrdinalIgnoreCase)) result.Add(await RelocationRules.ToDtoAsync(_db, r, parcel.Number, _user.Id, ct));
        return result;
    }
}

public sealed record AddRelocationCommand(
    Guid ParcelId, RelocationDetails Details, IReadOnlyList<StatusNoteInput>? StatusNotes, IReadOnlyList<RelocationAgentInput>? Agents, IReadOnlyList<RelocationContactInput>? Contacts,
    IReadOnlyList<RelocationEligibilityInput>? Eligibilities, IReadOnlyList<RelocationContactLogInput>? ContactLog) : IRequest<RelocationDto>, IRelocationFields;

public sealed class AddRelocationCommandValidator : RelocationFieldsValidator<AddRelocationCommand>
{
    public AddRelocationCommandValidator() => RuleFor(x => x.ParcelId).NotEmpty();
}

public sealed class AddRelocationCommandHandler : IRequestHandler<AddRelocationCommand, RelocationDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;

    public AddRelocationCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user)
    {
        _user = user;
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<RelocationDto> Handle(AddRelocationCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelRelocation, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        await RelocationRules.EnsureInterestAsync(_db, parcel, request.Details.PartyLinkId, ct);
        var number = request.Details.RelocationNumber.Trim();
        if (await _db.ParcelRelocations.AnyAsync(r => r.ParcelId == parcel.Id && r.RelocationNumber == number, ct))
            throw new ConflictException("relocation_number_exists", $"This parcel already has a relocation numbered '{number}'.");

        var r = new ParcelRelocation { ParcelId = parcel.Id };
        RelocationRules.Apply(r, request.Details);
        var children = RelocationRules.Build(request);
        r.Agents = children.Agents; r.Contacts = children.Contacts; r.Eligibilities = children.Eligibilities; r.ContactLog = children.ContactLog;
        _db.ParcelRelocations.Add(r);
        await StatusNoteRules.ReplaceAsync(_db, StatusNoteKinds.Relocation, r.Id, request.StatusNotes, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("relocation_number_exists", $"This parcel already has a relocation numbered '{number}'.");
        }
        await RelocationSync.RunAsync(_db, parcel.Id, ct);
        var fresh = await RelocationRules.WithChildren(_db.ParcelRelocations.AsNoTracking()).FirstAsync(x => x.Id == r.Id, ct);
        return await RelocationRules.ToDtoAsync(_db, fresh, parcel.Number, _user.Id, ct);
    }
}

public sealed record UpdateRelocationCommand(
    Guid ParcelId, Guid RelocationId, RelocationDetails Details, IReadOnlyList<StatusNoteInput>? StatusNotes, IReadOnlyList<RelocationAgentInput>? Agents,
    IReadOnlyList<RelocationContactInput>? Contacts, IReadOnlyList<RelocationEligibilityInput>? Eligibilities, IReadOnlyList<RelocationContactLogInput>? ContactLog)
    : IRequest<RelocationDto>, IRelocationFields;

public sealed class UpdateRelocationCommandValidator : RelocationFieldsValidator<UpdateRelocationCommand>
{
    public UpdateRelocationCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.RelocationId).NotEmpty();
    }
}

public sealed class UpdateRelocationCommandHandler : IRequestHandler<UpdateRelocationCommand, RelocationDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;

    public UpdateRelocationCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user)
    {
        _user = user;
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<RelocationDto> Handle(UpdateRelocationCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelRelocation, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var r = await RelocationRules.WithChildren(_db.ParcelRelocations).FirstOrDefaultAsync(x => x.Id == request.RelocationId && x.ParcelId == parcel.Id, ct)
                ?? throw new NotFoundException("Relocation", request.RelocationId);
        await RelocationRules.EnsureInterestAsync(_db, parcel, request.Details.PartyLinkId, ct);
        var number = request.Details.RelocationNumber.Trim();
        if (await _db.ParcelRelocations.AnyAsync(x => x.ParcelId == parcel.Id && x.RelocationNumber == number && x.Id != r.Id, ct))
            throw new ConflictException("relocation_number_exists", $"This parcel already has a relocation numbered '{number}'.");

        RelocationRules.Apply(r, request.Details);
        RelocationRules.RemoveChildren(_db, r);
        RelocationRules.Attach(_db, r.Id, RelocationRules.Build(request));
        await StatusNoteRules.ReplaceAsync(_db, StatusNoteKinds.Relocation, r.Id, request.StatusNotes, ct);
        await _db.SaveChangesAsync(ct);
        await RelocationSync.RunAsync(_db, parcel.Id, ct);

        var fresh = await RelocationRules.WithChildren(_db.ParcelRelocations.AsNoTracking()).FirstAsync(x => x.Id == r.Id, ct);
        return await RelocationRules.ToDtoAsync(_db, fresh, parcel.Number, _user.Id, ct);
    }
}

public sealed record RemoveRelocationCommand(Guid ParcelId, Guid RelocationId) : IRequest;

public sealed class RemoveRelocationCommandHandler : IRequestHandler<RemoveRelocationCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveRelocationCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveRelocationCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelRelocation, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var r = await RelocationRules.WithChildren(_db.ParcelRelocations).FirstOrDefaultAsync(x => x.Id == request.RelocationId && x.ParcelId == parcel.Id, ct)
                ?? throw new NotFoundException("Relocation", request.RelocationId);
        RelocationRules.RemoveChildren(_db, r);
        if (r.Payments.Any(p => p.Status is RelocationPaymentStatus.Approved or RelocationPaymentStatus.Paid))
            throw new ConflictException("relocation_has_payments", "This relocation has approved or paid payments, so it cannot be removed.");
        foreach (var p in r.Payments) _db.RelocationPaymentApprovals.RemoveRange(p.Approvals);
        _db.RelocationPayments.RemoveRange(r.Payments);
        await StatusNoteRules.DeleteAsync(_db, StatusNoteKinds.Relocation, r.Id, ct);
        _db.ParcelRelocations.Remove(r);
        await _db.SaveChangesAsync(ct);
    }
}
