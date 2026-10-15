using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.PlanReviews;

public sealed record PlanReviewDto(Guid Id, ReviewDiscipline Discipline, ReviewOutcome Outcome, DateOnly ReviewedOn, string ReviewerName, string? Notes, DateTime CreatedAt);

/// <summary>A parcel's plan reviews and whether the agency's gate is holding it (ROW-239).</summary>
public sealed record PlanReviewStatusDto(
    bool GateEnabled, string StepKey, IReadOnlyList<string> Missing, bool Satisfied, IReadOnlyList<PlanReviewDto> Reviews);

public sealed record PlanReviewGateSettingDto(bool Enabled, string StepKey);

internal static class PlanReviewShared
{
    public static async Task<bool> GateEnabledAsync(IRowDbContext db, CancellationToken ct) =>
        await db.AgencySettings.Select(s => s.PlanReviewGateEnabled).FirstOrDefaultAsync(ct);

    public static PlanReviewDto ToDto(this ParcelPlanReview r) => new(r.Id, r.Discipline, r.Outcome, r.ReviewedOn, r.ReviewerName, r.Notes, r.CreatedAt);

    public static async Task<PlanReviewStatusDto> StatusAsync(IRowDbContext db, Guid parcelId, CancellationToken ct)
    {
        var enabled = await GateEnabledAsync(db, ct);
        var reviews = await db.ParcelPlanReviews.Where(r => r.ParcelId == parcelId).ToListAsync(ct);
        var missing = PlanReviewGate.Missing(enabled, reviews.Select(r => new PlanReviewGate.ReviewFact(r.Discipline, r.Outcome, r.ReviewedOn, r.CreatedAt)));
        return new PlanReviewStatusDto(
            enabled, PlanReviewGate.StepKey, missing.Select(d => d.ToString()).ToList(), missing.Count == 0,
            reviews.OrderByDescending(r => r.ReviewedOn).ThenByDescending(r => r.CreatedAt).Select(r => r.ToDto()).ToList());
    }

    /// <summary>The disciplines still to approve, worded for a message; null when the parcel is free to move.</summary>
    public static async Task<string?> MissingTextAsync(IRowDbContext db, Guid parcelId, CancellationToken ct)
    {
        var status = await StatusAsync(db, parcelId, ct);
        return status.Missing.Count == 0 ? null : string.Join(" and ", status.Missing.Select(m => m.ToLowerInvariant()));
    }
}

// ── The agency setting ──────────────────────────────────────────────────

public sealed record GetPlanReviewGateQuery : IRequest<PlanReviewGateSettingDto>;

public sealed class GetPlanReviewGateQueryHandler : IRequestHandler<GetPlanReviewGateQuery, PlanReviewGateSettingDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public GetPlanReviewGateQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<PlanReviewGateSettingDto> Handle(GetPlanReviewGateQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        return new PlanReviewGateSettingDto(await PlanReviewShared.GateEnabledAsync(_db, ct), PlanReviewGate.StepKey);
    }
}

public sealed record SetPlanReviewGateCommand(bool Enabled) : IRequest<PlanReviewGateSettingDto>;

public sealed class SetPlanReviewGateCommandHandler : IRequestHandler<SetPlanReviewGateCommand, PlanReviewGateSettingDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public SetPlanReviewGateCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<PlanReviewGateSettingDto> Handle(SetPlanReviewGateCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);
        var settings = await _db.AgencySettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new AgencySettings();
            _db.AgencySettings.Add(settings);
        }
        settings.PlanReviewGateEnabled = request.Enabled;
        await _db.SaveChangesAsync(ct);
        return new PlanReviewGateSettingDto(settings.PlanReviewGateEnabled, PlanReviewGate.StepKey);
    }
}

// ── Reviews on a parcel ─────────────────────────────────────────────────

public sealed record GetParcelPlanReviewsQuery(Guid ParcelId) : IRequest<PlanReviewStatusDto>;

public sealed class GetParcelPlanReviewsQueryHandler : IRequestHandler<GetParcelPlanReviewsQuery, PlanReviewStatusDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public GetParcelPlanReviewsQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<PlanReviewStatusDto> Handle(GetParcelPlanReviewsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        return await PlanReviewShared.StatusAsync(_db, parcel.Id, ct);
    }
}

public sealed record AddPlanReviewCommand(Guid ParcelId, ReviewDiscipline Discipline, ReviewOutcome Outcome, DateOnly ReviewedOn, string ReviewerName, string? Notes)
    : IRequest<PlanReviewStatusDto>;

public sealed class AddPlanReviewCommandValidator : AbstractValidator<AddPlanReviewCommand>
{
    public AddPlanReviewCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.Discipline).IsInEnum().WithMessage("Choose engineer or surveyor.");
        RuleFor(x => x.Outcome).IsInEnum().WithMessage("Choose the outcome of the review.");
        RuleFor(x => x.ReviewedOn).Must(d => d.Year >= 1990 && d.Year <= 2100).WithMessage("Enter a valid date.");
        RuleFor(x => x.ReviewerName).NotEmpty().WithMessage("Enter who reviewed the plans.").MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Notes).NotEmpty().When(x => x.Outcome == ReviewOutcome.ChangesRequired).WithMessage("Say what has to change.");
    }
}

public sealed class AddPlanReviewCommandHandler : IRequestHandler<AddPlanReviewCommand, PlanReviewStatusDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public AddPlanReviewCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<PlanReviewStatusDto> Handle(AddPlanReviewCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var today = await _parcels.TodayForProjectAsync(parcel.ProjectId, ct);
        if (request.ReviewedOn > today)
            throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure(nameof(request.ReviewedOn), "The review date cannot be in the future.") });
        _db.ParcelPlanReviews.Add(new ParcelPlanReview
        {
            ParcelId = parcel.Id, Discipline = request.Discipline, Outcome = request.Outcome, ReviewedOn = request.ReviewedOn,
            ReviewerName = request.ReviewerName.Trim(), Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        });
        await _db.SaveChangesAsync(ct);
        return await PlanReviewShared.StatusAsync(_db, parcel.Id, ct);
    }
}

public sealed record RemovePlanReviewCommand(Guid ParcelId, Guid Id) : IRequest<PlanReviewStatusDto>;

public sealed class RemovePlanReviewCommandHandler : IRequestHandler<RemovePlanReviewCommand, PlanReviewStatusDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemovePlanReviewCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<PlanReviewStatusDto> Handle(RemovePlanReviewCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var review = await _db.ParcelPlanReviews.FirstOrDefaultAsync(r => r.Id == request.Id && r.ParcelId == parcel.Id, ct)
            ?? throw new NotFoundException("Plan review", request.Id);
        _db.ParcelPlanReviews.Remove(review);
        await _db.SaveChangesAsync(ct);
        return await PlanReviewShared.StatusAsync(_db, parcel.Id, ct);
    }
}
