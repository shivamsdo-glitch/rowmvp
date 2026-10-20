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

// ── Appraisal (Phase 1.5, ADR 0018) ─────────────────────────────────────

public sealed record AppraisalAmountInput(string AmountType, string? Description, decimal Amount);
public sealed record AppraisalAmountDto(Guid Id, string AmountType, string? Description, decimal Amount);

/// <summary><see cref="AppraiserCompany"/> is read-only (the appraiser's company in the Library); <see cref="AppraisalTotal"/> is the amounts added up.</summary>
public sealed record AppraisalDto(
    Guid Id, Guid ParcelId, string Number, string ParcelNumber, string? AppraisalType, string? Appraiser, string? AppraiserCompany, bool LandPlannerRequired,
    string? AssignedTo, DateOnly? RequestedDate, DateOnly? ReceivedDate, DateOnly? ReportDate, DateOnly? DueDate, DateOnly? SubmittedToAtpDate, DateOnly? EffectiveDate,
    IReadOnlyList<AppraisalAmountDto> Amounts, decimal AppraisalTotal,
    string? ReviewAppraiser, string? ReviewType, DateOnly? AppraisalReceivedDate, decimal? ApprovedJustCompensation, DateOnly? ReviewCompletedDate,
    DateOnly? ReviewSubmittedDate, string? ReviewRecommendation, decimal? ReviewRecommendedValue,
    DateOnly? SubmittedToJatDate, DateOnly? ApprovedByJatDate, bool FtaConcurrenceNeeded, DateTime UpdatedAt);

internal static class AppraisalRules
{
    public const decimal MaxAmount = 1_000_000_000_000m;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    /// <summary>One adder per parcel at a time in this process; the unique (parcel, sequence) index backstops other instances.</summary>
    public static SemaphoreSlim Gate(Guid parcelId) => Gates.GetOrAdd(parcelId, _ => new SemaphoreSlim(1, 1));

    public static string NumberOf(string parcelNumber, int sequence) => $"APR-{parcelNumber}-{sequence:000}";

    public static AppraisalReviewValues ReviewOf(ParcelAppraisal a) =>
        new(a.ReviewAppraiser, a.ReviewType, a.AppraisalReceivedDate, a.ApprovedJustCompensation, a.ReviewCompletedDate, a.ReviewSubmittedDate, a.ReviewRecommendation, a.ReviewRecommendedValue);

    public static AppraisalReviewValues ReviewOf(IAppraisalFields f) =>
        new(f.ReviewAppraiser, f.ReviewType, f.AppraisalReceivedDate, f.ApprovedJustCompensation, f.ReviewCompletedDate, f.ReviewSubmittedDate, f.ReviewRecommendation, f.ReviewRecommendedValue);

    public static ForbiddenException ReviewRestricted() =>
        new("Only the Appraisal Approver can change the appraisal review and the approved just compensation.");

    public static async Task<IReadOnlyDictionary<string, string?>> CompaniesAsync(IRowDbContext db, CancellationToken ct) =>
        (await db.LibraryAgents.ToListAsync(ct)).GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Company, StringComparer.OrdinalIgnoreCase);

    public static AppraisalDto ToDto(ParcelAppraisal a, string parcelNumber, IReadOnlyDictionary<string, string?> companies) =>
        new(a.Id, a.ParcelId, a.Number, parcelNumber, a.AppraisalType, a.Appraiser, a.Appraiser is not null && companies.TryGetValue(a.Appraiser, out var c) ? c : null, a.LandPlannerRequired,
            a.AssignedTo, a.RequestedDate, a.ReceivedDate, a.ReportDate, a.DueDate, a.SubmittedToAtpDate, a.EffectiveDate,
            a.Amounts.OrderBy(x => x.CreatedAt).ThenBy(x => x.AmountType).Select(x => new AppraisalAmountDto(x.Id, x.AmountType, x.Description, x.Amount)).ToList(), a.Amounts.Sum(x => x.Amount),
            a.ReviewAppraiser, a.ReviewType, a.AppraisalReceivedDate, a.ApprovedJustCompensation, a.ReviewCompletedDate, a.ReviewSubmittedDate, a.ReviewRecommendation, a.ReviewRecommendedValue,
            a.SubmittedToJatDate, a.ApprovedByJatDate, a.FtaConcurrenceNeeded, a.UpdatedAt);

    public static void Apply(ParcelAppraisal a, IAppraisalFields f)
    {
        a.AppraisalType = ParcelRecordHelpers.Clean(f.AppraisalType);
        a.Appraiser = ParcelRecordHelpers.Clean(f.Appraiser);
        a.LandPlannerRequired = f.LandPlannerRequired;
        a.AssignedTo = ParcelRecordHelpers.Clean(f.AssignedTo);
        a.RequestedDate = f.RequestedDate;
        a.ReceivedDate = f.ReceivedDate;
        a.ReportDate = f.ReportDate;
        a.DueDate = f.DueDate;
        a.SubmittedToAtpDate = f.SubmittedToAtpDate;
        a.EffectiveDate = f.EffectiveDate;
        a.SubmittedToJatDate = f.SubmittedToJatDate;
        a.ApprovedByJatDate = f.ApprovedByJatDate;
        a.FtaConcurrenceNeeded = f.FtaConcurrenceNeeded;
    }

    /// <summary>Writes the review fields. Callers have already checked that the person may.</summary>
    public static void ApplyReview(ParcelAppraisal a, IAppraisalFields f)
    {
        a.ReviewAppraiser = ParcelRecordHelpers.Clean(f.ReviewAppraiser);
        a.ReviewType = ParcelRecordHelpers.Clean(f.ReviewType);
        a.AppraisalReceivedDate = f.AppraisalReceivedDate;
        a.ApprovedJustCompensation = f.ApprovedJustCompensation;
        a.ReviewCompletedDate = f.ReviewCompletedDate;
        a.ReviewSubmittedDate = f.ReviewSubmittedDate;
        a.ReviewRecommendation = ParcelRecordHelpers.Clean(f.ReviewRecommendation);
        a.ReviewRecommendedValue = f.ReviewRecommendedValue;
    }

    public static List<ParcelAppraisalAmount> BuildAmounts(IReadOnlyList<AppraisalAmountInput>? input) =>
        (input ?? Array.Empty<AppraisalAmountInput>()).Select(x => new ParcelAppraisalAmount
        {
            AmountType = x.AmountType.Trim(), Description = ParcelRecordHelpers.Clean(x.Description), Amount = x.Amount,
        }).ToList();
}

public interface IAppraisalFields
{
    string? AppraisalType { get; }
    string? Appraiser { get; }
    bool LandPlannerRequired { get; }
    string? AssignedTo { get; }
    DateOnly? RequestedDate { get; }
    DateOnly? ReceivedDate { get; }
    DateOnly? ReportDate { get; }
    DateOnly? DueDate { get; }
    DateOnly? SubmittedToAtpDate { get; }
    DateOnly? EffectiveDate { get; }
    IReadOnlyList<AppraisalAmountInput>? Amounts { get; }
    string? ReviewAppraiser { get; }
    string? ReviewType { get; }
    DateOnly? AppraisalReceivedDate { get; }
    decimal? ApprovedJustCompensation { get; }
    DateOnly? ReviewCompletedDate { get; }
    DateOnly? ReviewSubmittedDate { get; }
    string? ReviewRecommendation { get; }
    decimal? ReviewRecommendedValue { get; }
    DateOnly? SubmittedToJatDate { get; }
    DateOnly? ApprovedByJatDate { get; }
    bool FtaConcurrenceNeeded { get; }
}

public abstract class AppraisalFieldsValidator<T> : AbstractValidator<T> where T : IAppraisalFields
{
    protected AppraisalFieldsValidator()
    {
        RuleFor(x => x.AppraisalType).MaximumLength(100);
        RuleFor(x => x.Appraiser).MaximumLength(200);
        RuleFor(x => x.AssignedTo).MaximumLength(200);
        RuleFor(x => x.ReviewAppraiser).MaximumLength(200);
        RuleFor(x => x.ReviewType).MaximumLength(100);
        RuleFor(x => x.ReviewRecommendation).MaximumLength(100);
        RuleFor(x => x.ReceivedDate).GreaterThanOrEqualTo(x => x.RequestedDate!.Value).When(x => x.RequestedDate is not null && x.ReceivedDate is not null)
            .WithMessage("The appraisal cannot be received before it is requested.");
        RuleFor(x => x.ApprovedByJatDate).GreaterThanOrEqualTo(x => x.SubmittedToJatDate!.Value).When(x => x.SubmittedToJatDate is not null && x.ApprovedByJatDate is not null)
            .WithMessage("The appraisal cannot be approved by the JAT before it is submitted.");
        RuleFor(x => x.ApprovedJustCompensation).InclusiveBetween(0m, AppraisalRules.MaxAmount).When(x => x.ApprovedJustCompensation is not null).WithMessage("The amount must be zero or more.");
        RuleFor(x => x.ReviewRecommendedValue).InclusiveBetween(0m, AppraisalRules.MaxAmount).When(x => x.ReviewRecommendedValue is not null).WithMessage("The amount must be zero or more.");
        RuleFor(x => x.Amounts).Must(a => a is null || a.Count <= 50).WithMessage("An appraisal can have at most 50 amounts.");
        RuleForEach(x => x.Amounts).ChildRules(a =>
        {
            a.RuleFor(i => i.AmountType).NotEmpty().WithMessage("Each amount needs an amount type.").MaximumLength(100);
            a.RuleFor(i => i.Description).MaximumLength(500);
            a.RuleFor(i => i.Amount).InclusiveBetween(0m, AppraisalRules.MaxAmount).WithMessage("The amount must be zero or more.");
        });
    }
}

public sealed record ListAppraisalsQuery(Guid ParcelId) : IRequest<IReadOnlyList<AppraisalDto>>;

public sealed class ListAppraisalsQueryHandler : IRequestHandler<ListAppraisalsQuery, IReadOnlyList<AppraisalDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public ListAppraisalsQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<IReadOnlyList<AppraisalDto>> Handle(ListAppraisalsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelAppraisal, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var rows = await _db.ParcelAppraisals.Include(a => a.Amounts).Where(a => a.ParcelId == parcel.Id).ToListAsync(ct);
        var companies = await AppraisalRules.CompaniesAsync(_db, ct);
        return rows.OrderBy(a => a.Sequence).Select(a => AppraisalRules.ToDto(a, parcel.Number, companies)).ToList();
    }
}

public sealed record AddAppraisalCommand(
    Guid ParcelId, string? AppraisalType, string? Appraiser, bool LandPlannerRequired, string? AssignedTo, DateOnly? RequestedDate, DateOnly? ReceivedDate, DateOnly? ReportDate,
    DateOnly? DueDate, DateOnly? SubmittedToAtpDate, DateOnly? EffectiveDate, IReadOnlyList<AppraisalAmountInput>? Amounts, string? ReviewAppraiser, string? ReviewType,
    DateOnly? AppraisalReceivedDate, decimal? ApprovedJustCompensation, DateOnly? ReviewCompletedDate, DateOnly? ReviewSubmittedDate, string? ReviewRecommendation,
    decimal? ReviewRecommendedValue, DateOnly? SubmittedToJatDate, DateOnly? ApprovedByJatDate, bool FtaConcurrenceNeeded) : IRequest<AppraisalDto>, IAppraisalFields;

public sealed class AddAppraisalCommandValidator : AppraisalFieldsValidator<AddAppraisalCommand>
{
    public AddAppraisalCommandValidator() => RuleFor(x => x.ParcelId).NotEmpty();
}

public sealed class AddAppraisalCommandHandler : IRequestHandler<AddAppraisalCommand, AppraisalDto>
{
    private const int MaxAttempts = 6;
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;

    public AddAppraisalCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _user = user;
    }

    public async Task<AppraisalDto> Handle(AddAppraisalCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelAppraisal, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        // A new appraisal starts with an empty review; only the review roles may start it filled in.
        if (AppraisalReviewAccess.ReviewChanged(default, AppraisalRules.ReviewOf(request)) && !AppraisalReviewAccess.MayChangeReview(_user.Roles))
            throw AppraisalRules.ReviewRestricted();

        var gate = AppraisalRules.Gate(parcel.Id);
        await gate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var sequence = (await _db.ParcelAppraisals.Where(a => a.ParcelId == parcel.Id).MaxAsync(a => (int?)a.Sequence, ct) ?? 0) + 1;
                var a = new ParcelAppraisal { ParcelId = parcel.Id, Sequence = sequence, Number = AppraisalRules.NumberOf(parcel.Number, sequence), Amounts = AppraisalRules.BuildAmounts(request.Amounts) };
                AppraisalRules.Apply(a, request);
                AppraisalRules.ApplyReview(a, request);
                _db.ParcelAppraisals.Add(a);
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    _db.ChangeTracker.Clear();
                    continue;
                }
                return AppraisalRules.ToDto(a, parcel.Number, await AppraisalRules.CompaniesAsync(_db, ct));
            }
        }
        finally
        {
            gate.Release();
        }
        throw new ConflictException("appraisal_busy", "The system is busy numbering appraisals. Please try again.");
    }
}

public sealed record UpdateAppraisalCommand(
    Guid ParcelId, Guid AppraisalId, string? AppraisalType, string? Appraiser, bool LandPlannerRequired, string? AssignedTo, DateOnly? RequestedDate, DateOnly? ReceivedDate,
    DateOnly? ReportDate, DateOnly? DueDate, DateOnly? SubmittedToAtpDate, DateOnly? EffectiveDate, IReadOnlyList<AppraisalAmountInput>? Amounts, string? ReviewAppraiser,
    string? ReviewType, DateOnly? AppraisalReceivedDate, decimal? ApprovedJustCompensation, DateOnly? ReviewCompletedDate, DateOnly? ReviewSubmittedDate,
    string? ReviewRecommendation, decimal? ReviewRecommendedValue, DateOnly? SubmittedToJatDate, DateOnly? ApprovedByJatDate, bool FtaConcurrenceNeeded)
    : IRequest<AppraisalDto>, IAppraisalFields;

public sealed class UpdateAppraisalCommandValidator : AppraisalFieldsValidator<UpdateAppraisalCommand>
{
    public UpdateAppraisalCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.AppraisalId).NotEmpty();
    }
}

public sealed class UpdateAppraisalCommandHandler : IRequestHandler<UpdateAppraisalCommand, AppraisalDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;

    public UpdateAppraisalCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _user = user;
    }

    public async Task<AppraisalDto> Handle(UpdateAppraisalCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelAppraisal, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var a = await _db.ParcelAppraisals.Include(x => x.Amounts).FirstOrDefaultAsync(x => x.Id == request.AppraisalId && x.ParcelId == parcel.Id, ct)
                ?? throw new NotFoundException("Appraisal", request.AppraisalId);

        var mayReview = AppraisalReviewAccess.MayChangeReview(_user.Roles);
        if (!mayReview && AppraisalReviewAccess.ReviewChanged(AppraisalRules.ReviewOf(a), AppraisalRules.ReviewOf(request))) throw AppraisalRules.ReviewRestricted();

        AppraisalRules.Apply(a, request);
        if (mayReview) AppraisalRules.ApplyReview(a, request);
        _db.ParcelAppraisalAmounts.RemoveRange(a.Amounts);
        var amounts = AppraisalRules.BuildAmounts(request.Amounts);
        foreach (var x in amounts) x.AppraisalId = a.Id;
        _db.ParcelAppraisalAmounts.AddRange(amounts);
        await _db.SaveChangesAsync(ct);

        var fresh = await _db.ParcelAppraisals.Include(x => x.Amounts).AsNoTracking().FirstAsync(x => x.Id == a.Id, ct);
        return AppraisalRules.ToDto(fresh, parcel.Number, await AppraisalRules.CompaniesAsync(_db, ct));
    }
}

public sealed record RemoveAppraisalCommand(Guid ParcelId, Guid AppraisalId) : IRequest;

public sealed class RemoveAppraisalCommandHandler : IRequestHandler<RemoveAppraisalCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveAppraisalCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveAppraisalCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelAppraisal, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var a = await _db.ParcelAppraisals.Include(x => x.Amounts).FirstOrDefaultAsync(x => x.Id == request.AppraisalId && x.ParcelId == parcel.Id, ct)
                ?? throw new NotFoundException("Appraisal", request.AppraisalId);
        _db.ParcelAppraisalAmounts.RemoveRange(a.Amounts);
        _db.ParcelAppraisals.Remove(a);
        await _db.SaveChangesAsync(ct);
    }
}
