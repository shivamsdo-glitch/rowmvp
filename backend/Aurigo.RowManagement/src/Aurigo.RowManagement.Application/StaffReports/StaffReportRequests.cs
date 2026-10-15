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

namespace Aurigo.RowManagement.Application.StaffReports;

public sealed record StaffReportDto(
    Guid Id, Guid ParcelId, ReportKind Kind, ReportStatus Status, string Title, string Body, DateTime? SubmittedAt, bool SubmittedByMe,
    DateTime? DecidedAt, string? DecisionNote, bool CanEdit, DateTime UpdatedAt);

internal static class StaffReportShared
{
    public static StaffReportDto ToDto(this ParcelStaffReport r, Guid me) =>
        new(r.Id, r.ParcelId, r.Kind, r.Status, r.Title, r.Body, r.SubmittedAt, r.SubmittedBy == me, r.DecidedAt, r.DecisionNote, ApprovalRoute.CanEdit(r.Status), r.UpdatedAt);

    public static async Task<ParcelStaffReport> LoadAsync(IRowDbContext db, Guid parcelId, Guid id, CancellationToken ct) =>
        await db.ParcelStaffReports.FirstOrDefaultAsync(r => r.Id == id && r.ParcelId == parcelId, ct) ?? throw new NotFoundException("Report", id);

    public static void ThrowAlreadyExists(ReportKind kind) =>
        throw new ConflictException("report_exists", $"This parcel already has a {(kind == ReportKind.StaffReport ? "staff report" : "certificate of necessity")}. Change that one, or delete it if it was not approved.");
}

public sealed record ListStaffReportsQuery(Guid ParcelId) : IRequest<IReadOnlyList<StaffReportDto>>;

public sealed class ListStaffReportsQueryHandler : IRequestHandler<ListStaffReportsQuery, IReadOnlyList<StaffReportDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;

    public ListStaffReportsQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _user = user;
    }

    public async Task<IReadOnlyList<StaffReportDto>> Handle(ListStaffReportsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var rows = await _db.ParcelStaffReports.Where(r => r.ParcelId == parcel.Id).ToListAsync(ct);
        return rows.OrderBy(r => r.Kind).ThenByDescending(r => r.CreatedAt).Select(r => r.ToDto(_user.Id)).ToList();
    }
}

public sealed record CreateStaffReportCommand(Guid ParcelId, ReportKind Kind, string Title, string Body) : IRequest<StaffReportDto>;

public sealed class CreateStaffReportCommandValidator : AbstractValidator<CreateStaffReportCommand>
{
    public CreateStaffReportCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Choose the kind of report.");
        RuleFor(x => x.Title).NotEmpty().WithMessage("Enter a title.").MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty().WithMessage("Write the report.").MaximumLength(8000);
    }
}

public sealed class CreateStaffReportCommandHandler : IRequestHandler<CreateStaffReportCommand, StaffReportDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;

    public CreateStaffReportCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _user = user;
    }

    public async Task<StaffReportDto> Handle(CreateStaffReportCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        if (await _db.ParcelStaffReports.AnyAsync(r => r.ParcelId == parcel.Id && r.Kind == request.Kind && r.Status != ReportStatus.Rejected, ct))
            StaffReportShared.ThrowAlreadyExists(request.Kind);
        var report = new ParcelStaffReport { ParcelId = parcel.Id, Kind = request.Kind, Title = request.Title.Trim(), Body = request.Body.Trim() };
        _db.ParcelStaffReports.Add(report);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) // the partial unique index: two people created the same kind at once
        {
            StaffReportShared.ThrowAlreadyExists(request.Kind);
        }
        return report.ToDto(_user.Id);
    }
}

public sealed record UpdateStaffReportCommand(Guid ParcelId, Guid Id, string Title, string Body) : IRequest<StaffReportDto>;

public sealed class UpdateStaffReportCommandValidator : AbstractValidator<UpdateStaffReportCommand>
{
    public UpdateStaffReportCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().WithMessage("Enter a title.").MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty().WithMessage("Write the report.").MaximumLength(8000);
    }
}

public sealed class UpdateStaffReportCommandHandler : IRequestHandler<UpdateStaffReportCommand, StaffReportDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;

    public UpdateStaffReportCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _user = user;
    }

    public async Task<StaffReportDto> Handle(UpdateStaffReportCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var report = await StaffReportShared.LoadAsync(_db, parcel.Id, request.Id, ct);
        if (!ApprovalRoute.CanEdit(report.Status))
            throw new ConflictException("report_locked", $"A {report.Status.ToString().ToLowerInvariant()} report cannot be changed.");
        report.Title = request.Title.Trim();
        report.Body = request.Body.Trim();
        await _db.SaveChangesAsync(ct);
        return report.ToDto(_user.Id);
    }
}

public sealed record SubmitStaffReportCommand(Guid ParcelId, Guid Id) : IRequest<StaffReportDto>;

public sealed class SubmitStaffReportCommandHandler : IRequestHandler<SubmitStaffReportCommand, StaffReportDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;

    public SubmitStaffReportCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user, TimeProvider clock)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _user = user;
        _clock = clock;
    }

    public async Task<StaffReportDto> Handle(SubmitStaffReportCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var report = await StaffReportShared.LoadAsync(_db, parcel.Id, request.Id, ct);
        if (!ApprovalRoute.CanSubmit(report.Status))
            throw new ConflictException("report_not_submittable", $"A {report.Status.ToString().ToLowerInvariant()} report cannot be submitted.");
        report.Status = ReportStatus.Submitted;
        report.SubmittedAt = _clock.GetUtcNow().UtcDateTime;
        report.SubmittedBy = _user.Id;
        report.DecidedAt = null;
        report.DecidedBy = null;
        report.DecisionNote = null;
        await _db.SaveChangesAsync(ct);
        return report.ToDto(_user.Id);
    }
}

/// <summary>Approve or reject a submitted report. Approving is an Approvals job, and never the submitter's own (ROW-240).</summary>
public sealed record DecideStaffReportCommand(Guid ParcelId, Guid Id, bool Approve, string? Note) : IRequest<StaffReportDto>;

public sealed class DecideStaffReportCommandValidator : AbstractValidator<DecideStaffReportCommand>
{
    public DecideStaffReportCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.Note).NotEmpty().When(x => !x.Approve).WithMessage("Say why the report is rejected.");
    }
}

public sealed class DecideStaffReportCommandHandler : IRequestHandler<DecideStaffReportCommand, StaffReportDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;

    public DecideStaffReportCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user, TimeProvider clock)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _user = user;
        _clock = clock;
    }

    public async Task<StaffReportDto> Handle(DecideStaffReportCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Approvals, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var report = await StaffReportShared.LoadAsync(_db, parcel.Id, request.Id, ct);
        if (!ApprovalRoute.CanDecide(report.Status))
            throw new ConflictException("report_not_submitted", "Only a submitted report can be approved or rejected.");
        if (ApprovalRoute.IsOwnSubmission(report.SubmittedBy, _user.Id))
            throw new ForbiddenException("You submitted this report, so someone else has to approve or reject it.");
        report.Status = request.Approve ? ReportStatus.Approved : ReportStatus.Rejected;
        report.DecidedAt = _clock.GetUtcNow().UtcDateTime;
        report.DecidedBy = _user.Id;
        report.DecisionNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        await _db.SaveChangesAsync(ct);
        return report.ToDto(_user.Id);
    }
}

public sealed record DeleteStaffReportCommand(Guid ParcelId, Guid Id) : IRequest;

public sealed class DeleteStaffReportCommandHandler : IRequestHandler<DeleteStaffReportCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public DeleteStaffReportCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(DeleteStaffReportCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var report = await StaffReportShared.LoadAsync(_db, parcel.Id, request.Id, ct);
        if (!ApprovalRoute.CanDelete(report.Status))
            throw new ConflictException("report_locked", $"A {report.Status.ToString().ToLowerInvariant()} report cannot be deleted.");
        _db.ParcelStaffReports.Remove(report);
        await _db.SaveChangesAsync(ct);
    }
}
