using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Mappers;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Parcels;

// Every command below returns the whole updated parcel, so a screen can redraw from one response.

// ── Acquisition interests (ROW-234) ─────────────────────────────────────

public interface IInterestFields
{
    InterestType Type { get; }
    decimal AreaSqFt { get; }
    string? Notes { get; }
}

public abstract class InterestFieldsValidator<T> : AbstractValidator<T> where T : IInterestFields
{
    protected InterestFieldsValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.AreaSqFt).InclusiveBetween(0m, ParcelRules.MaxArea).WithMessage("Area must be zero or more square feet.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed record SaveParcelInterestCommand(Guid ParcelId, Guid? InterestId, InterestType Type, decimal AreaSqFt, string? Notes)
    : IRequest<ParcelDto>, IInterestFields;

public sealed class SaveParcelInterestCommandValidator : InterestFieldsValidator<SaveParcelInterestCommand>
{
    public SaveParcelInterestCommandValidator() => RuleFor(x => x.ParcelId).NotEmpty();
}

public sealed class SaveParcelInterestCommandHandler : IRequestHandler<SaveParcelInterestCommand, ParcelDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public SaveParcelInterestCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelDto> Handle(SaveParcelInterestCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, request.InterestId is null ? PermissionAction.Create : PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);

        // An interest is a take from the parcel, so it cannot be larger than the parcel once the parcel's area is known.
        if (parcel.AreaSqFt > 0m && request.AreaSqFt > parcel.AreaSqFt)
            throw new BusinessRuleException($"The interest ({request.AreaSqFt:N2} sq ft) is larger than the parcel ({parcel.AreaSqFt:N2} sq ft).");

        ParcelInterest interest;
        if (request.InterestId is { } id)
            interest = parcel.Interests.FirstOrDefault(i => i.Id == id) ?? throw new NotFoundException("Interest", id);
        else
        {
            // Added through the set: a child with a preassigned id reached only via the parent's list would be treated as existing.
            interest = new ParcelInterest { ParcelId = parcel.Id };
            _db.ParcelInterests.Add(interest);
        }
        interest.Type = request.Type;
        interest.AreaSqFt = request.AreaSqFt;
        interest.Notes = ParcelRules.Clean(request.Notes);

        await _db.SaveChangesAsync(ct);
        return await _parcels.ToDtoAsync(parcel, ct);
    }
}

public sealed record DeleteParcelInterestCommand(Guid ParcelId, Guid InterestId) : IRequest<ParcelDto>;

public sealed class DeleteParcelInterestCommandHandler : IRequestHandler<DeleteParcelInterestCommand, ParcelDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public DeleteParcelInterestCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelDto> Handle(DeleteParcelInterestCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var interest = parcel.Interests.FirstOrDefault(i => i.Id == request.InterestId) ?? throw new NotFoundException("Interest", request.InterestId);
        parcel.Interests.Remove(interest);
        _db.ParcelInterests.Remove(interest);
        await _db.SaveChangesAsync(ct);
        return await _parcels.ToDtoAsync(parcel, ct);
    }
}

// ── Milestones (ROW-010) ────────────────────────────────────────────────

/// <summary>Adds a custom milestone (the standard ones already exist). Names are unique per parcel, ignoring case.</summary>
public sealed record AddParcelMilestoneCommand(Guid ParcelId, string Name, DateOnly? DueOn, DateOnly? CompletedOn, string? Notes) : IRequest<ParcelDto>;

public sealed class AddParcelMilestoneCommandValidator : AbstractValidator<AddParcelMilestoneCommand>
{
    public AddParcelMilestoneCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(100);
        RuleFor(x => x.DueOn).Must(MilestoneDates.IsPlausible).WithMessage("Enter a valid due date.");
        RuleFor(x => x.CompletedOn).Must(MilestoneDates.IsPlausible).WithMessage("Enter a valid completion date.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

/// <summary>Date rules shared by the milestone commands. A completion date is a fact, so it cannot be in the future.</summary>
internal static class MilestoneDates
{
    public const int FirstYear = 1990;
    public const int LastYear = 2100;

    public static bool IsPlausible(DateOnly? date) => date is null || (date.Value.Year >= FirstYear && date.Value.Year <= LastYear);

    public static void EnsureNotInFuture(DateOnly? completedOn, DateOnly today)
    {
        if (completedOn is { } d && d > today)
            throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("CompletedOn", "A milestone cannot be completed in the future.") });
    }
}

public sealed class AddParcelMilestoneCommandHandler : IRequestHandler<AddParcelMilestoneCommand, ParcelDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public AddParcelMilestoneCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelDto> Handle(AddParcelMilestoneCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var name = request.Name.Trim();
        MilestoneDates.EnsureNotInFuture(request.CompletedOn, await _parcels.TodayForProjectAsync(parcel.ProjectId, ct));
        if (parcel.Milestones.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ConflictException("milestone_exists", $"This parcel already has a milestone named '{name}'.");

        _db.ParcelMilestones.Add(new ParcelMilestone
        {
            ParcelId = parcel.Id, Name = name, DueOn = request.DueOn, CompletedOn = request.CompletedOn, Notes = ParcelRules.Clean(request.Notes),
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("milestone_exists", $"This parcel already has a milestone named '{name}'.");
        }
        return await _parcels.ToDtoAsync(parcel, ct);
    }
}

/// <summary>Sets a milestone's target date, completion date and notes. A milestone's name never changes.</summary>
public sealed record UpdateParcelMilestoneCommand(Guid ParcelId, Guid MilestoneId, DateOnly? DueOn, DateOnly? CompletedOn, string? Notes) : IRequest<ParcelDto>;

public sealed class UpdateParcelMilestoneCommandValidator : AbstractValidator<UpdateParcelMilestoneCommand>
{
    public UpdateParcelMilestoneCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.MilestoneId).NotEmpty();
        RuleFor(x => x.DueOn).Must(MilestoneDates.IsPlausible).WithMessage("Enter a valid due date.");
        RuleFor(x => x.CompletedOn).Must(MilestoneDates.IsPlausible).WithMessage("Enter a valid completion date.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class UpdateParcelMilestoneCommandHandler : IRequestHandler<UpdateParcelMilestoneCommand, ParcelDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public UpdateParcelMilestoneCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelDto> Handle(UpdateParcelMilestoneCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var milestone = parcel.Milestones.FirstOrDefault(m => m.Id == request.MilestoneId) ?? throw new NotFoundException("Milestone", request.MilestoneId);
        MilestoneDates.EnsureNotInFuture(request.CompletedOn, await _parcels.TodayForProjectAsync(parcel.ProjectId, ct));

        // The notice's date lives with its delivery method on the parcel; letting the milestone drift from it would give
        // the deadline and the synopsis two different answers.
        if (string.Equals(milestone.Name, MilestoneNames.NoticeToOwner, StringComparison.OrdinalIgnoreCase) && request.CompletedOn != milestone.CompletedOn)
            throw new InvalidRequestException("milestone_notice_managed",
                "The Notice to owner date is set by recording the notice (date and delivery method), not by editing this milestone.");
        // A parcel exists because it was set up; correcting that date is fine, clearing it is not.
        if (string.Equals(milestone.Name, MilestoneNames.ParcelSetUp, StringComparison.OrdinalIgnoreCase) && request.CompletedOn is null)
            throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("CompletedOn", "The set-up date cannot be cleared.") });

        milestone.DueOn = request.DueOn;
        milestone.CompletedOn = request.CompletedOn;
        milestone.Notes = ParcelRules.Clean(request.Notes);
        await _db.SaveChangesAsync(ct);
        return await _parcels.ToDtoAsync(parcel, ct);
    }
}

/// <summary>Removes a custom milestone. The standard milestones that deadline rules depend on cannot be removed.</summary>
public sealed record DeleteParcelMilestoneCommand(Guid ParcelId, Guid MilestoneId) : IRequest<ParcelDto>;

public sealed class DeleteParcelMilestoneCommandHandler : IRequestHandler<DeleteParcelMilestoneCommand, ParcelDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public DeleteParcelMilestoneCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelDto> Handle(DeleteParcelMilestoneCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var milestone = parcel.Milestones.FirstOrDefault(m => m.Id == request.MilestoneId) ?? throw new NotFoundException("Milestone", request.MilestoneId);
        if (MilestoneNames.Standard.Contains(milestone.Name, StringComparer.OrdinalIgnoreCase))
            throw new ConflictException("milestone_standard", $"'{milestone.Name}' is a standard milestone and cannot be removed.");
        parcel.Milestones.Remove(milestone);
        _db.ParcelMilestones.Remove(milestone);
        await _db.SaveChangesAsync(ct);
        return await _parcels.ToDtoAsync(parcel, ct);
    }
}

// ── Site visits (ROW-236) ───────────────────────────────────────────────

public sealed record AddParcelSiteVisitCommand(Guid ParcelId, DateOnly VisitedOn, string? Notes, bool OwnerContactConfirmed) : IRequest<ParcelDto>;

public sealed class AddParcelSiteVisitCommandValidator : AbstractValidator<AddParcelSiteVisitCommand>
{
    public AddParcelSiteVisitCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.VisitedOn).Must(d => d.Year >= 1990).WithMessage("Enter the date of the visit.");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class AddParcelSiteVisitCommandHandler : IRequestHandler<AddParcelSiteVisitCommand, ParcelDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public AddParcelSiteVisitCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelDto> Handle(AddParcelSiteVisitCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        _db.ParcelSiteVisits.Add(new ParcelSiteVisit
        {
            ParcelId = parcel.Id, VisitedOn = request.VisitedOn, Notes = ParcelRules.Clean(request.Notes), OwnerContactConfirmed = request.OwnerContactConfirmed,
        });
        await _db.SaveChangesAsync(ct);
        return await _parcels.ToDtoAsync(parcel, ct);
    }
}

public sealed record DeleteParcelSiteVisitCommand(Guid ParcelId, Guid VisitId) : IRequest<ParcelDto>;

public sealed class DeleteParcelSiteVisitCommandHandler : IRequestHandler<DeleteParcelSiteVisitCommand, ParcelDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public DeleteParcelSiteVisitCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelDto> Handle(DeleteParcelSiteVisitCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var visit = parcel.SiteVisits.FirstOrDefault(v => v.Id == request.VisitId) ?? throw new NotFoundException("Site visit", request.VisitId);
        parcel.SiteVisits.Remove(visit);
        _db.ParcelSiteVisits.Remove(visit);
        await _db.SaveChangesAsync(ct);
        return await _parcels.ToDtoAsync(parcel, ct);
    }
}

// ── Notice to Owner (ROW-237) ───────────────────────────────────────────

/// <summary>
/// Records when and how the written Notice to Owner was delivered, and completes the "Notice to owner" milestone with that date
/// (which starts any deadline that runs from it). A null date clears both.
/// </summary>
public sealed record RecordNoticeToOwnerCommand(Guid ParcelId, DateOnly? IssuedOn, NoticeMethod? Method) : IRequest<ParcelDto>;

public sealed class RecordNoticeToOwnerCommandValidator : AbstractValidator<RecordNoticeToOwnerCommand>
{
    public RecordNoticeToOwnerCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.IssuedOn).Must(d => d is null || d.Value.Year >= 1990).WithMessage("Enter the date the notice was issued.");
        RuleFor(x => x.Method).NotNull().When(x => x.IssuedOn is not null).WithMessage("Choose how the notice was delivered.");
        RuleFor(x => x.Method).IsInEnum().When(x => x.Method is not null);
        RuleFor(x => x.Method).Null().When(x => x.IssuedOn is null).WithMessage("Remove the delivery method when clearing the notice.");
    }
}

public sealed class RecordNoticeToOwnerCommandHandler : IRequestHandler<RecordNoticeToOwnerCommand, ParcelDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RecordNoticeToOwnerCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelDto> Handle(RecordNoticeToOwnerCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);

        parcel.NoticeIssuedOn = request.IssuedOn;
        parcel.NoticeMethod = request.IssuedOn is null ? null : request.Method;

        var milestone = parcel.Milestones.FirstOrDefault(m => string.Equals(m.Name, MilestoneNames.NoticeToOwner, StringComparison.OrdinalIgnoreCase));
        if (milestone is null)
        {
            milestone = new ParcelMilestone { ParcelId = parcel.Id, Name = MilestoneNames.NoticeToOwner };
            _db.ParcelMilestones.Add(milestone);
        }
        milestone.CompletedOn = request.IssuedOn;

        await _db.SaveChangesAsync(ct);
        return await _parcels.ToDtoAsync(parcel, ct);
    }
}

// ── Ordered step gating with override (ROW-072) ─────────────────────────

/// <summary>
/// Moves a parcel to another step of its workflow. The next step in order needs nothing more; anything else is an override that
/// needs a permitted role and a written reason. Steps that start offers need the project's acquisition gate open, always.
/// </summary>
public sealed record AdvanceParcelStepCommand(Guid ParcelId, string TargetStepKey, string? OverrideReason) : IRequest<ParcelDto>;

public sealed class AdvanceParcelStepCommandValidator : AbstractValidator<AdvanceParcelStepCommand>
{
    public AdvanceParcelStepCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.TargetStepKey).NotEmpty().WithMessage("Choose the step to move to.").MaximumLength(60);
        RuleFor(x => x.OverrideReason).MaximumLength(1000);
    }
}

public sealed class AdvanceParcelStepCommandHandler : IRequestHandler<AdvanceParcelStepCommand, ParcelDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public AdvanceParcelStepCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelDto> Handle(AdvanceParcelStepCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var project = await _parcels.LoadProjectAsync(parcel.ProjectId, ct);
        var steps = ParcelService.StepInfos(await _parcels.TemplateAsync(parcel.AcquisitionType, ct));

        var result = StepGate.CanAdvance(
            steps, parcel.CurrentStepKey, request.TargetStepKey, project.EvaluateGate().Unlocked,
            await _parcels.CanOverrideAsync(ct), request.OverrideReason);

        switch (result.Outcome)
        {
            case StepMoveOutcome.Allowed:
            case StepMoveOutcome.AllowedWithOverride:
                break;
            case StepMoveOutcome.OverrideNotPermitted:
                throw new ForbiddenException(result.Message);
            case StepMoveOutcome.BlockedByAcquisitionGate:
                throw new ConflictException("acquisition_gate_closed", result.Message);
            case StepMoveOutcome.OverrideRequiresReason:
                throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure(nameof(request.OverrideReason), result.Message) });
            default:
                throw new InvalidRequestException("invalid_step", result.Message);
        }

        _db.ParcelStepEvents.Add(new ParcelStepEvent
        {
            ParcelId = parcel.Id,
            FromStepKey = parcel.CurrentStepKey,
            ToStepKey = request.TargetStepKey,
            IsOverride = result.IsOverride,
            Reason = result.IsOverride ? request.OverrideReason!.Trim() : null,
        });
        parcel.CurrentStepKey = request.TargetStepKey;
        await _db.SaveChangesAsync(ct);

        // The event was added through the set (not the navigation), so reload the parcel's history for the response.
        return await _parcels.ToDtoAsync(await _parcels.LoadAsync(parcel.Id, ct), ct);
    }
}
