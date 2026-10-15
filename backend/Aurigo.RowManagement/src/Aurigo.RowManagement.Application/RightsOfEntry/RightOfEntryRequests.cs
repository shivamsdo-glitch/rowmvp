using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.RightsOfEntry;

public sealed record RightOfEntryDto(
    Guid Id, Guid ParcelId, RightOfEntryStatus Status, string EffectiveStatus, DateOnly? RequestedOn, DateOnly? GrantedOn, DateOnly? ExpiresOn,
    string? GrantedBy, string? Purpose, string? Notes, DateTime UpdatedAt);

/// <summary>One row of the report of every right of entry (ROW-077).</summary>
public sealed record RightOfEntryReportRowDto(
    Guid Id, Guid ParcelId, string ParcelNumber, string? ParcelAddress, Guid ProjectId, string ProjectNumber, RightOfEntryStatus Status, string EffectiveStatus,
    DateOnly? RequestedOn, DateOnly? GrantedOn, DateOnly? ExpiresOn, int? DaysUntilExpiry, string? GrantedBy, string? Purpose);

internal static class RightOfEntryMapper
{
    public static RightOfEntryDto ToDto(this RightOfEntry r, DateOnly today) =>
        new(r.Id, r.ParcelId, r.Status, RightOfEntryRules.Effective(r.Status, r.ExpiresOn, today), r.RequestedOn, r.GrantedOn, r.ExpiresOn, r.GrantedBy, r.Purpose, r.Notes, r.UpdatedAt);

    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static void ThrowIfProblems(RightOfEntryStatus status, DateOnly? requestedOn, DateOnly? grantedOn, DateOnly? expiresOn, DateOnly today)
    {
        var problems = RightOfEntryRules.Problems(status, requestedOn, grantedOn, expiresOn, today);
        if (problems.Count == 0) return;
        throw new ValidationException(problems.Select(p => new FluentValidation.Results.ValidationFailure(
            p.Contains("given") || p.Contains("granted") || p.Contains("permission", StringComparison.OrdinalIgnoreCase) ? "GrantedOn" : "RequestedOn", p)));
    }
}

// ── On a parcel ─────────────────────────────────────────────────────────

public sealed record ListParcelRightsOfEntryQuery(Guid ParcelId) : IRequest<IReadOnlyList<RightOfEntryDto>>;

public sealed class ListParcelRightsOfEntryQueryHandler : IRequestHandler<ListParcelRightsOfEntryQuery, IReadOnlyList<RightOfEntryDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public ListParcelRightsOfEntryQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<IReadOnlyList<RightOfEntryDto>> Handle(ListParcelRightsOfEntryQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var today = await _parcels.TodayForProjectAsync(parcel.ProjectId, ct);
        var rows = await _db.RightsOfEntry.Where(r => r.ParcelId == parcel.Id).ToListAsync(ct);
        return rows.OrderByDescending(r => r.CreatedAt).Select(r => r.ToDto(today)).ToList();
    }
}

public interface IRightOfEntryFields
{
    RightOfEntryStatus Status { get; }
    DateOnly? RequestedOn { get; }
    DateOnly? GrantedOn { get; }
    DateOnly? ExpiresOn { get; }
    string? GrantedBy { get; }
    string? Purpose { get; }
    string? Notes { get; }
}

public abstract class RightOfEntryFieldsValidator<T> : AbstractValidator<T> where T : IRightOfEntryFields
{
    protected RightOfEntryFieldsValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        foreach (var rule in new Func<T, DateOnly?>[] { x => x.RequestedOn, x => x.GrantedOn, x => x.ExpiresOn })
            RuleFor(x => rule(x)).Must(d => d is null || (d.Value.Year >= 1990 && d.Value.Year <= 2100)).WithMessage("Enter a valid date.");
        RuleFor(x => x.GrantedBy).MaximumLength(200);
        RuleFor(x => x.Purpose).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed record AddRightOfEntryCommand(
    Guid ParcelId, RightOfEntryStatus Status, DateOnly? RequestedOn, DateOnly? GrantedOn, DateOnly? ExpiresOn, string? GrantedBy, string? Purpose, string? Notes)
    : IRequest<RightOfEntryDto>, IRightOfEntryFields;

public sealed class AddRightOfEntryCommandValidator : RightOfEntryFieldsValidator<AddRightOfEntryCommand>
{
    public AddRightOfEntryCommandValidator() => RuleFor(x => x.ParcelId).NotEmpty();
}

public sealed class AddRightOfEntryCommandHandler : IRequestHandler<AddRightOfEntryCommand, RightOfEntryDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public AddRightOfEntryCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<RightOfEntryDto> Handle(AddRightOfEntryCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var today = await _parcels.TodayForProjectAsync(parcel.ProjectId, ct);
        RightOfEntryMapper.ThrowIfProblems(request.Status, request.RequestedOn, request.GrantedOn, request.ExpiresOn, today);
        var roe = new RightOfEntry
        {
            ParcelId = parcel.Id, Status = request.Status, RequestedOn = request.RequestedOn, GrantedOn = request.GrantedOn, ExpiresOn = request.ExpiresOn,
            GrantedBy = RightOfEntryMapper.Clean(request.GrantedBy), Purpose = RightOfEntryMapper.Clean(request.Purpose), Notes = RightOfEntryMapper.Clean(request.Notes),
        };
        _db.RightsOfEntry.Add(roe);
        await _db.SaveChangesAsync(ct);
        return roe.ToDto(today);
    }
}

public sealed record UpdateRightOfEntryCommand(
    Guid ParcelId, Guid Id, RightOfEntryStatus Status, DateOnly? RequestedOn, DateOnly? GrantedOn, DateOnly? ExpiresOn, string? GrantedBy, string? Purpose, string? Notes)
    : IRequest<RightOfEntryDto>, IRightOfEntryFields;

public sealed class UpdateRightOfEntryCommandValidator : RightOfEntryFieldsValidator<UpdateRightOfEntryCommand>
{
    public UpdateRightOfEntryCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class UpdateRightOfEntryCommandHandler : IRequestHandler<UpdateRightOfEntryCommand, RightOfEntryDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public UpdateRightOfEntryCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<RightOfEntryDto> Handle(UpdateRightOfEntryCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var roe = await _db.RightsOfEntry.FirstOrDefaultAsync(r => r.Id == request.Id && r.ParcelId == parcel.Id, ct) ?? throw new NotFoundException("Right of entry", request.Id);
        var today = await _parcels.TodayForProjectAsync(parcel.ProjectId, ct);
        RightOfEntryMapper.ThrowIfProblems(request.Status, request.RequestedOn, request.GrantedOn, request.ExpiresOn, today);
        roe.Status = request.Status;
        roe.RequestedOn = request.RequestedOn;
        roe.GrantedOn = request.GrantedOn;
        roe.ExpiresOn = request.ExpiresOn;
        roe.GrantedBy = RightOfEntryMapper.Clean(request.GrantedBy);
        roe.Purpose = RightOfEntryMapper.Clean(request.Purpose);
        roe.Notes = RightOfEntryMapper.Clean(request.Notes);
        await _db.SaveChangesAsync(ct);
        return roe.ToDto(today);
    }
}

public sealed record RemoveRightOfEntryCommand(Guid ParcelId, Guid Id) : IRequest;

public sealed class RemoveRightOfEntryCommandHandler : IRequestHandler<RemoveRightOfEntryCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveRightOfEntryCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveRightOfEntryCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var roe = await _db.RightsOfEntry.FirstOrDefaultAsync(r => r.Id == request.Id && r.ParcelId == parcel.Id, ct) ?? throw new NotFoundException("Right of entry", request.Id);
        _db.RightsOfEntry.Remove(roe);
        await _db.SaveChangesAsync(ct);
    }
}

// ── The report of all rights of entry (ROW-077) ─────────────────────────

/// <summary>
/// Every right of entry on a live parcel. <see cref="Status"/> may be a stored status or "Expired"; <see cref="ExpiringWithinDays"/>
/// keeps granted rights that end within that many days. Each row's dates are counted where its project is.
/// </summary>
public sealed record RightsOfEntryReportQuery(Guid? ProjectId, string? Status, int? ExpiringWithinDays, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<RightOfEntryReportRowDto>>;

public sealed class RightsOfEntryReportQueryValidator : AbstractValidator<RightsOfEntryReportQuery>
{
    private static readonly string[] Allowed = Enum.GetNames<RightOfEntryStatus>().Append("Expired").ToArray();

    public RightsOfEntryReportQueryValidator()
    {
        RuleFor(x => x.Status).Must(s => s is null || Allowed.Contains(s, StringComparer.OrdinalIgnoreCase)).WithMessage("Choose a status from the list.");
        RuleFor(x => x.ExpiringWithinDays).InclusiveBetween(0, 365).When(x => x.ExpiringWithinDays is not null);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class RightsOfEntryReportQueryHandler : IRequestHandler<RightsOfEntryReportQuery, PagedResult<RightOfEntryReportRowDto>>
{
    private const int MaxRows = 2000;
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RightsOfEntryReportQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<PagedResult<RightOfEntryReportRowDto>> Handle(RightsOfEntryReportQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        var query = from r in _db.RightsOfEntry
                    join p in _db.Parcels on r.ParcelId equals p.Id
                    join pr in _db.Projects on p.ProjectId equals pr.Id
                    where p.State == ParcelState.Active
                    select new { r, ParcelNumber = p.Number, p.Address, p.ProjectId, ProjectNumber = pr.Number };
        if (request.ProjectId is { } projectId) query = query.Where(x => x.ProjectId == projectId);
        if (request.Status is { } status && Enum.TryParse<RightOfEntryStatus>(status, true, out var stored)) query = query.Where(x => x.r.Status == stored);
        else if (string.Equals(request.Status, "Expired", StringComparison.OrdinalIgnoreCase)) query = query.Where(x => x.r.Status == RightOfEntryStatus.Granted && x.r.ExpiresOn != null);
        if (request.ExpiringWithinDays is not null) query = query.Where(x => x.r.Status == RightOfEntryStatus.Granted && x.r.ExpiresOn != null);

        var rows = await query.OrderBy(x => x.ProjectNumber).ThenBy(x => x.ParcelNumber).ThenBy(x => x.r.CreatedAt).Take(MaxRows).ToListAsync(ct);
        var todays = new Dictionary<Guid, DateOnly>();
        foreach (var projectKey in rows.Select(x => x.ProjectId).Distinct())
            todays[projectKey] = await _parcels.TodayForProjectAsync(projectKey, ct);

        var shaped = rows.Select(x =>
        {
            var today = todays[x.ProjectId];
            var effective = RightOfEntryRules.Effective(x.r.Status, x.r.ExpiresOn, today);
            int? days = x.r.Status == RightOfEntryStatus.Granted && x.r.ExpiresOn is { } e ? e.DayNumber - today.DayNumber : null;
            return (Row: new RightOfEntryReportRowDto(x.r.Id, x.r.ParcelId, x.ParcelNumber, x.Address, x.ProjectId, x.ProjectNumber, x.r.Status, effective, x.r.RequestedOn, x.r.GrantedOn,
                x.r.ExpiresOn, days, x.r.GrantedBy, x.r.Purpose), Soon: RightOfEntryRules.ExpiresSoon(x.r.Status, x.r.ExpiresOn, today, request.ExpiringWithinDays ?? 0));
        });
        if (string.Equals(request.Status, "Expired", StringComparison.OrdinalIgnoreCase)) shaped = shaped.Where(x => x.Row.EffectiveStatus == "Expired");
        else if (string.Equals(request.Status, "Granted", StringComparison.OrdinalIgnoreCase)) shaped = shaped.Where(x => x.Row.EffectiveStatus == "Granted");
        if (request.ExpiringWithinDays is not null) shaped = shaped.Where(x => x.Soon);

        var list = shaped.Select(x => x.Row).ToList();
        return new PagedResult<RightOfEntryReportRowDto>(list.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList(), list.Count, request.Page, request.PageSize);
    }
}
