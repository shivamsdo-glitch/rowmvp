using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.AcquisitionRequests;

public sealed record AcquisitionRequestDto(
    Guid Id, Guid? ProjectId, string? ProjectNumber, RequesterType RequesterType, string RequesterName, string? RequesterEmail, string Property,
    string? DesiredParameters, decimal? EstimatedAmount, AcquisitionRequestStatus Status, bool SubmittedByMe, DateTime CreatedAt, DateTime? DecidedAt,
    string? DecisionNote);

internal static class AcquisitionRequestShared
{
    // The submitter is the entity's audit creator.
    public static AcquisitionRequestDto ToDto(this AcquisitionRequest r, string? projectNumber, Guid me) =>
        new(r.Id, r.ProjectId, projectNumber, r.RequesterType, r.RequesterName, r.RequesterEmail, r.Property, r.DesiredParameters, r.EstimatedAmount,
            r.Status, r.CreatedBy == me, r.CreatedAt, r.DecidedAt, r.DecisionNote);

    public static async Task<AcquisitionRequestDto> DtoAsync(IRowDbContext db, AcquisitionRequest r, Guid me, CancellationToken ct)
    {
        var number = r.ProjectId is { } p ? await db.Projects.Where(x => x.Id == p).Select(x => x.Number).FirstOrDefaultAsync(ct) : null;
        return r.ToDto(number, me);
    }

    public static async Task<AcquisitionRequest> LoadAsync(IRowDbContext db, Guid id, CancellationToken ct) =>
        await db.AcquisitionRequests.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Acquisition request", id);
}

public sealed record ListAcquisitionRequestsQuery(string? Status, int Page = 1, int PageSize = 50) : IRequest<PagedResult<AcquisitionRequestDto>>;

public sealed class ListAcquisitionRequestsQueryValidator : AbstractValidator<ListAcquisitionRequestsQuery>
{
    public ListAcquisitionRequestsQueryValidator()
    {
        RuleFor(x => x.Status).Must(s => s is null || Enum.TryParse<AcquisitionRequestStatus>(s, true, out _)).WithMessage("Choose a status from the list.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class ListAcquisitionRequestsQueryHandler : IRequestHandler<ListAcquisitionRequestsQuery, PagedResult<AcquisitionRequestDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentUser _user;

    public ListAcquisitionRequestsQueryHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _user = user;
    }

    public async Task<PagedResult<AcquisitionRequestDto>> Handle(ListAcquisitionRequestsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Approvals, PermissionAction.Read, ct);
        var query = _db.AcquisitionRequests.AsQueryable();
        if (request.Status is { } s) { var status = Enum.Parse<AcquisitionRequestStatus>(s, true); query = query.Where(r => r.Status == status); }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(r => r.CreatedAt).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var projectIds = rows.Where(r => r.ProjectId != null).Select(r => r.ProjectId!.Value).Distinct().ToList();
        var numbers = await _db.Projects.Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, ct);
        return new PagedResult<AcquisitionRequestDto>(
            rows.Select(r => r.ToDto(r.ProjectId is { } p ? numbers.GetValueOrDefault(p) : null, _user.Id)).ToList(), total, request.Page, request.PageSize);
    }
}

public sealed record GetAcquisitionRequestQuery(Guid Id) : IRequest<AcquisitionRequestDto>;

public sealed class GetAcquisitionRequestQueryHandler : IRequestHandler<GetAcquisitionRequestQuery, AcquisitionRequestDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentUser _user;

    public GetAcquisitionRequestQueryHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _user = user;
    }

    public async Task<AcquisitionRequestDto> Handle(GetAcquisitionRequestQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Approvals, PermissionAction.Read, ct);
        return await AcquisitionRequestShared.DtoAsync(_db, await AcquisitionRequestShared.LoadAsync(_db, request.Id, ct), _user.Id, ct);
    }
}

/// <summary>Someone inside or outside the agency asks for a property to be acquired (ROW-080).</summary>
public sealed record SubmitAcquisitionRequestCommand(
    Guid? ProjectId, RequesterType RequesterType, string RequesterName, string? RequesterEmail, string Property, string? DesiredParameters, decimal? EstimatedAmount)
    : IRequest<AcquisitionRequestDto>;

public sealed class SubmitAcquisitionRequestCommandValidator : AbstractValidator<SubmitAcquisitionRequestCommand>
{
    public SubmitAcquisitionRequestCommandValidator()
    {
        RuleFor(x => x.RequesterType).IsInEnum().WithMessage("Choose whether the request is from inside or outside the agency.");
        RuleFor(x => x.RequesterName).NotEmpty().WithMessage("Enter who is asking.").MaximumLength(200);
        RuleFor(x => x.RequesterEmail).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.RequesterEmail)).WithMessage("Enter a valid email address.");
        RuleFor(x => x.Property).NotEmpty().WithMessage("Say which property is wanted.").MaximumLength(500);
        RuleFor(x => x.DesiredParameters).MaximumLength(2000);
        RuleFor(x => x.EstimatedAmount).InclusiveBetween(0m, 100_000_000_000m).When(x => x.EstimatedAmount is not null).WithMessage("Enter an amount of zero or more.");
    }
}

public sealed class SubmitAcquisitionRequestCommandHandler : IRequestHandler<SubmitAcquisitionRequestCommand, AcquisitionRequestDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentUser _user;

    public SubmitAcquisitionRequestCommandHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _user = user;
    }

    public async Task<AcquisitionRequestDto> Handle(SubmitAcquisitionRequestCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Create, ct);
        if (request.ProjectId is { } p && !await _db.Projects.AnyAsync(x => x.Id == p, ct)) throw new NotFoundException("Project", p);
        var entity = new AcquisitionRequest
        {
            ProjectId = request.ProjectId, RequesterType = request.RequesterType, RequesterName = request.RequesterName.Trim(),
            RequesterEmail = string.IsNullOrWhiteSpace(request.RequesterEmail) ? null : request.RequesterEmail.Trim(),
            Property = request.Property.Trim(), DesiredParameters = string.IsNullOrWhiteSpace(request.DesiredParameters) ? null : request.DesiredParameters.Trim(),
            EstimatedAmount = request.EstimatedAmount,
        };
        _db.AcquisitionRequests.Add(entity);
        await _db.SaveChangesAsync(ct);
        return await AcquisitionRequestShared.DtoAsync(_db, entity, _user.Id, ct);
    }
}

/// <summary>Approve or reject a submitted request. The person who entered it cannot decide it (ROW-080).</summary>
public sealed record DecideAcquisitionRequestCommand(Guid Id, bool Approve, string? Note) : IRequest<AcquisitionRequestDto>;

public sealed class DecideAcquisitionRequestCommandValidator : AbstractValidator<DecideAcquisitionRequestCommand>
{
    public DecideAcquisitionRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.Note).NotEmpty().When(x => !x.Approve).WithMessage("Say why the request is rejected.");
    }
}

public sealed class DecideAcquisitionRequestCommandHandler : IRequestHandler<DecideAcquisitionRequestCommand, AcquisitionRequestDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;

    public DecideAcquisitionRequestCommandHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentUser user, TimeProvider clock)
    {
        _db = db;
        _permissions = permissions;
        _user = user;
        _clock = clock;
    }

    public async Task<AcquisitionRequestDto> Handle(DecideAcquisitionRequestCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Approvals, PermissionAction.Update, ct);
        var entity = await AcquisitionRequestShared.LoadAsync(_db, request.Id, ct);
        if (entity.Status != AcquisitionRequestStatus.Submitted)
            throw new ConflictException("request_decided", $"This request is already {entity.Status.ToString().ToLowerInvariant()}.");
        if (ApprovalRoute.IsOwnSubmission(entity.CreatedBy, _user.Id))
            throw new ForbiddenException("You entered this request, so someone else has to approve or reject it.");
        entity.Status = request.Approve ? AcquisitionRequestStatus.Approved : AcquisitionRequestStatus.Rejected;
        entity.DecidedAt = _clock.GetUtcNow().UtcDateTime;
        entity.DecidedBy = _user.Id;
        entity.DecisionNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        await _db.SaveChangesAsync(ct);
        return await AcquisitionRequestShared.DtoAsync(_db, entity, _user.Id, ct);
    }
}

/// <summary>The person who entered a request takes it back while it is still waiting.</summary>
public sealed record WithdrawAcquisitionRequestCommand(Guid Id) : IRequest<AcquisitionRequestDto>;

public sealed class WithdrawAcquisitionRequestCommandHandler : IRequestHandler<WithdrawAcquisitionRequestCommand, AcquisitionRequestDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentUser _user;

    public WithdrawAcquisitionRequestCommandHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _user = user;
    }

    public async Task<AcquisitionRequestDto> Handle(WithdrawAcquisitionRequestCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Update, ct);
        var entity = await AcquisitionRequestShared.LoadAsync(_db, request.Id, ct);
        if (entity.CreatedBy != _user.Id) throw new ForbiddenException("Only the person who entered a request can withdraw it.");
        if (entity.Status != AcquisitionRequestStatus.Submitted)
            throw new ConflictException("request_decided", $"This request is already {entity.Status.ToString().ToLowerInvariant()}.");
        entity.Status = AcquisitionRequestStatus.Withdrawn;
        await _db.SaveChangesAsync(ct);
        return await AcquisitionRequestShared.DtoAsync(_db, entity, _user.Id, ct);
    }
}
