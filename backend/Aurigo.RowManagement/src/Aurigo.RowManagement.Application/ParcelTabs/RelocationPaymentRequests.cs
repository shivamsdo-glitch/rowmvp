using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Application.ParcelRecords;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.ParcelTabs;

// ── Relocation payments (ADR 0026): typed amount, configurable approval chain, Finance marks paid ──

public sealed record RelocationBandInput(decimal MinAmount, decimal? MaxAmount, IReadOnlyList<string> Roles);
public sealed record RelocationBandDto(decimal MinAmount, decimal? MaxAmount, IReadOnlyList<string> Roles);
public sealed record RelocationBandsDto(IReadOnlyList<RelocationBandDto> Bands, bool IsDefault);

internal static class RelocationPaymentShared
{
    public const decimal MaxAmount = 1_000_000_000m;

    public static async Task<ApprovalChainConfig> ChainAsync(IRowDbContext db, CancellationToken ct)
    {
        var rows = await db.RelocationPaymentBands.AsNoTracking().OrderBy(b => b.MinAmount).ToListAsync(ct);
        return rows.Count == 0 ? RelocationCalc.DefaultPaymentChain : new ApprovalChainConfig(rows.Select(b => new ApprovalBand(b.MinAmount, b.MaxAmount, RelocationCalc.ParseRoles(b.Roles))).ToList());
    }

    public static async Task<(ParcelRelocation Relocation, Parcel Parcel)> LoadAsync(IRowDbContext db, ParcelService parcels, Guid parcelId, Guid relocationId, CancellationToken ct)
    {
        var parcel = await parcels.LoadActiveAsync(parcelId, ct);
        var r = await RelocationRules.WithChildren(db.ParcelRelocations).FirstOrDefaultAsync(x => x.Id == relocationId && x.ParcelId == parcel.Id, ct)
                ?? throw new NotFoundException("Relocation", relocationId);
        return (r, parcel);
    }

    public static async Task<RelocationDto> DtoAsync(IRowDbContext db, Guid relocationId, string parcelNumber, Guid me, CancellationToken ct)
    {
        var fresh = await RelocationRules.WithChildren(db.ParcelRelocations.AsNoTracking()).FirstAsync(x => x.Id == relocationId, ct);
        return await RelocationRules.ToDtoAsync(db, fresh, parcelNumber, me, ct);
    }

    public static RelocationPayment Find(ParcelRelocation r, Guid paymentId) =>
        r.Payments.FirstOrDefault(p => p.Id == paymentId) ?? throw new NotFoundException("Relocation payment", paymentId);
}

// ── Request ──

public sealed record RequestRelocationPaymentCommand(Guid ParcelId, Guid RelocationId, string PaymentType, decimal Amount, bool IsAdvance, bool Taxable, string? Notes) : IRequest<RelocationDto>;

public sealed class RequestRelocationPaymentCommandValidator : AbstractValidator<RequestRelocationPaymentCommand>
{
    public RequestRelocationPaymentCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.RelocationId).NotEmpty();
        RuleFor(x => x.PaymentType).NotEmpty().WithMessage("Choose the type of payment.").MaximumLength(100);
        RuleFor(x => x.Amount).GreaterThan(0m).WithMessage("Enter an amount above zero.").LessThanOrEqualTo(RelocationPaymentShared.MaxAmount).WithMessage("That amount is too large.");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class RequestRelocationPaymentCommandHandler : IRequestHandler<RequestRelocationPaymentCommand, RelocationDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;

    public RequestRelocationPaymentCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _user = user;
    }

    public async Task<RelocationDto> Handle(RequestRelocationPaymentCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelRelocation, PermissionAction.Update, ct);
        var (r, parcel) = await RelocationPaymentShared.LoadAsync(_db, _parcels, request.ParcelId, request.RelocationId, ct);
        if (r.IneligibleForRelocation) throw new ConflictException("relocation_ineligible", "This displacee is marked ineligible for relocation, so no payment can be requested.");
        var chain = await RelocationPaymentShared.ChainAsync(_db, ct);
        ApprovalRequirement need;
        try { need = ApprovalChain.Evaluate(request.Amount, chain); }
        catch (InvalidOperationException) { throw new ConflictException("approval_chain_incomplete", "The relocation payment approval chain has a gap for this amount. An administrator must fix it in Configuration."); }
        var payment = new RelocationPayment
        {
            RelocationId = r.Id, PaymentType = request.PaymentType.Trim(), Amount = request.Amount, IsAdvance = request.IsAdvance, Taxable = request.Taxable,
            Notes = ParcelRecordHelpers.Clean(request.Notes), RequiredRoles = string.Join(",", need.Steps),
        };
        _db.RelocationPayments.Add(payment);
        await _db.SaveChangesAsync(ct);
        return await RelocationPaymentShared.DtoAsync(_db, r.Id, parcel.Number, _user.Id, ct);
    }
}

// ── Sign or reject ──

public sealed record DecideRelocationPaymentCommand(Guid ParcelId, Guid RelocationId, Guid PaymentId, bool Approve, string? Note) : IRequest<RelocationDto>;

public sealed class DecideRelocationPaymentCommandValidator : AbstractValidator<DecideRelocationPaymentCommand>
{
    public DecideRelocationPaymentCommandValidator()
    {
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.Note).NotEmpty().When(x => !x.Approve).WithMessage("Say why the payment is rejected.");
    }
}

public sealed class DecideRelocationPaymentCommandHandler : IRequestHandler<DecideRelocationPaymentCommand, RelocationDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;

    public DecideRelocationPaymentCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user, TimeProvider clock)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _user = user;
        _clock = clock;
    }

    public async Task<RelocationDto> Handle(DecideRelocationPaymentCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelRelocation, PermissionAction.Read, ct);
        var (r, parcel) = await RelocationPaymentShared.LoadAsync(_db, _parcels, request.ParcelId, request.RelocationId, ct);
        var p = RelocationPaymentShared.Find(r, request.PaymentId);
        if (p.Status != RelocationPaymentStatus.Requested) throw new ConflictException("payment_decided", $"This payment is already {p.Status.ToString().ToLowerInvariant()}.");
        if (ApprovalRoute.IsOwnSubmission(p.CreatedBy, _user.Id)) throw new ForbiddenException("You requested this payment, so someone else has to approve or reject it.");

        var required = RelocationCalc.ParseRoles(p.RequiredRoles);
        var requirement = new ApprovalRequirement(new ApprovalBand(0, null, required), required);
        var signed = p.Approvals.Select(a => a.Role).ToList();
        if (!ApprovalChain.CanApprove(requirement, signed, _user.Roles) && !_user.Roles.Contains(RoleNames.Admin))
            throw new ForbiddenException($"The next sign-off on this payment is {ApprovalChain.NextApprover(requirement, signed)}.");
        if (p.Approvals.Any(a => a.ApprovedBy == _user.Id)) throw new ForbiddenException("You have already signed this payment; the next step needs someone else.");

        var now = _clock.GetUtcNow().UtcDateTime;
        if (!request.Approve)
        {
            p.Status = RelocationPaymentStatus.Rejected;
            p.DecisionNote = request.Note!.Trim();
        }
        else
        {
            // An Admin signing in place of the next role is recorded as that role, so the chain still reads in order.
            var step = ApprovalChain.NextApprover(requirement, signed)!;
            _db.RelocationPaymentApprovals.Add(new RelocationPaymentApproval { PaymentId = p.Id, Role = step, ApprovedBy = _user.Id, ApprovedAt = now });
            if (ApprovalChain.IsComplete(requirement, signed.Append(step).ToList())) p.Status = RelocationPaymentStatus.Approved;
            p.DecisionNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        }
        await _db.SaveChangesAsync(ct);
        return await RelocationPaymentShared.DtoAsync(_db, r.Id, parcel.Number, _user.Id, ct);
    }
}

// ── Mark paid (Finance) ──

public sealed record MarkRelocationPaymentPaidCommand(Guid ParcelId, Guid RelocationId, Guid PaymentId, string? Reference) : IRequest<RelocationDto>;

public sealed class MarkRelocationPaymentPaidCommandValidator : AbstractValidator<MarkRelocationPaymentPaidCommand>
{
    public MarkRelocationPaymentPaidCommandValidator() => RuleFor(x => x.Reference).MaximumLength(200);
}

public sealed class MarkRelocationPaymentPaidCommandHandler : IRequestHandler<MarkRelocationPaymentPaidCommand, RelocationDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;
    private readonly TimeProvider _clock;

    public MarkRelocationPaymentPaidCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user, TimeProvider clock)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _user = user;
        _clock = clock;
    }

    public async Task<RelocationDto> Handle(MarkRelocationPaymentPaidCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Payments, PermissionAction.Update, ct);
        var (r, parcel) = await RelocationPaymentShared.LoadAsync(_db, _parcels, request.ParcelId, request.RelocationId, ct);
        var p = RelocationPaymentShared.Find(r, request.PaymentId);
        if (p.Status != RelocationPaymentStatus.Approved) throw new ConflictException("payment_not_approved", "Only an approved payment can be marked paid.");
        p.Status = RelocationPaymentStatus.Paid;
        p.PaidAt = _clock.GetUtcNow().UtcDateTime;
        p.PaymentReference = ParcelRecordHelpers.Clean(request.Reference);
        await _db.SaveChangesAsync(ct);
        return await RelocationPaymentShared.DtoAsync(_db, r.Id, parcel.Number, _user.Id, ct);
    }
}

// ── Approval bands (Configuration page) ──

public sealed record GetRelocationBandsQuery : IRequest<RelocationBandsDto>;

public sealed class GetRelocationBandsQueryHandler : IRequestHandler<GetRelocationBandsQuery, RelocationBandsDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public GetRelocationBandsQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<RelocationBandsDto> Handle(GetRelocationBandsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelRelocation, PermissionAction.Read, ct);
        var isDefault = !await _db.RelocationPaymentBands.AnyAsync(ct);
        var chain = await RelocationPaymentShared.ChainAsync(_db, ct);
        return new(chain.Bands.OrderBy(b => b.MinAmount).Select(b => new RelocationBandDto(b.MinAmount, b.MaxAmount, b.RequiredRoles)).ToList(), isDefault);
    }
}

public sealed record SaveRelocationBandsCommand(IReadOnlyList<RelocationBandInput> Bands) : IRequest<RelocationBandsDto>;

public sealed class SaveRelocationBandsCommandValidator : AbstractValidator<SaveRelocationBandsCommand>
{
    public SaveRelocationBandsCommandValidator()
    {
        RuleFor(x => x.Bands).NotNull().Must(b => b is null || b.Count <= 20).WithMessage("At most 20 bands.");
        RuleFor(x => x.Bands).Custom((bands, ctx) =>
        {
            if (bands is null) return;
            foreach (var problem in ApprovalChain.Validate(new ApprovalChainConfig(bands.Select(b => new ApprovalBand(b.MinAmount, b.MaxAmount, b.Roles ?? Array.Empty<string>())).ToList())))
                ctx.AddFailure(problem);
        });
    }
}

public sealed class SaveRelocationBandsCommandHandler : IRequestHandler<SaveRelocationBandsCommand, RelocationBandsDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public SaveRelocationBandsCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<RelocationBandsDto> Handle(SaveRelocationBandsCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);
        _db.RelocationPaymentBands.RemoveRange(await _db.RelocationPaymentBands.ToListAsync(ct));
        foreach (var b in request.Bands.OrderBy(b => b.MinAmount))
            _db.RelocationPaymentBands.Add(new RelocationPaymentBand { MinAmount = b.MinAmount, MaxAmount = b.MaxAmount, Roles = string.Join(",", b.Roles) });
        await _db.SaveChangesAsync(ct);
        return new(request.Bands.OrderBy(b => b.MinAmount).Select(b => new RelocationBandDto(b.MinAmount, b.MaxAmount, b.Roles)).ToList(), false);
    }
}

// ── Parcel progress (for the parcel summary and the Relocation tab) ──

public sealed record GetParcelRelocationProgressQuery(Guid ParcelId) : IRequest<ParcelRelocationProgressDto>;

public sealed class GetParcelRelocationProgressQueryHandler : IRequestHandler<GetParcelRelocationProgressQuery, ParcelRelocationProgressDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public GetParcelRelocationProgressQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ParcelRelocationProgressDto> Handle(GetParcelRelocationProgressQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelRelocation, PermissionAction.Read, ct);
        return await RelocationSync.ProgressAsync(_db, await _parcels.LoadAsync(request.ParcelId, ct), ct);
    }
}

// ── Work screen: relocations across parcels ──

public sealed record RelocationOverviewRow(
    Guid Id, Guid ParcelId, string ParcelNumber, string? Address, string? OwnerName, string Number, string DisplaceeName, string RelocationType, string Stage,
    string? AssignedTo, DateOnly? TargetVacateDate, DateOnly? ClaimDeadline, int OpenPayments);

public sealed record RelocationOverviewQuery(string? Search) : IRequest<IReadOnlyList<RelocationOverviewRow>>;

public sealed class RelocationOverviewQueryHandler : IRequestHandler<RelocationOverviewQuery, IReadOnlyList<RelocationOverviewRow>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public RelocationOverviewQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<RelocationOverviewRow>> Handle(RelocationOverviewQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Relocations, PermissionAction.Read, ct);
        var term = request.Search?.Trim().ToLower();
        if (term is { Length: > OverviewQueryHandlers.MaxSearchLength }) term = term[..OverviewQueryHandlers.MaxSearchLength];
        List<Guid>? ids = null;
        if (!string.IsNullOrEmpty(term))
        {
            var byParcel = await _db.Parcels.AsNoTracking().Where(p => p.Number.ToLower().Contains(term) || (p.Address != null && p.Address.ToLower().Contains(term))).Select(p => p.Id).ToListAsync(ct);
            ids = byParcel.Union(await Parties.ParcelOwners.ParcelIdsByOwnerAsync(_db, term, ct)).ToList();
        }
        var q = _db.ParcelRelocations.AsNoTracking().Include(r => r.Payments).AsQueryable();
        if (term is { Length: > 0 }) q = q.Where(r => ids!.Contains(r.ParcelId) || r.RelocationNumber.ToLower().Contains(term) || r.DisplaceeName.ToLower().Contains(term));
        var rows = await q.OrderByDescending(r => r.CreatedAt).Take(OverviewQueryHandlers.MaxRows).ToListAsync(ct);
        var wanted = rows.Select(r => r.ParcelId).Distinct().ToList();
        var parcels = await _db.Parcels.AsNoTracking().Where(p => wanted.Contains(p.Id)).Select(p => new { p.Id, p.Number, p.Address }).ToDictionaryAsync(p => p.Id, ct);
        var owners = (await Parties.ParcelOwners.NamesAsync(_db, wanted, null, ct)).ToDictionary(o => o.Key, o => string.Join("; ", o.Value));
        return rows.Select(r =>
        {
            var p = parcels.GetValueOrDefault(r.ParcelId);
            return new RelocationOverviewRow(r.Id, r.ParcelId, p?.Number ?? "", p?.Address, owners.GetValueOrDefault(r.ParcelId), r.RelocationNumber, r.DisplaceeName, r.RelocationType,
                RelocationCalc.Stage(r.GinMailedDate, r.InitialInterview, r.NoticeOfEligibility, r.ReplacementHousingStudyDate, r.Notice90Day, r.Notice30DayVacate, r.ActualVacateDate, r.RelocationComplete, r.IneligibleForRelocation),
                r.AssignedTo, r.TargetVacateDate, RelocationCalc.ClaimDeadline(r.ActualVacateDate), r.Payments.Count(x => x.Status is RelocationPaymentStatus.Requested or RelocationPaymentStatus.Approved));
        }).ToList();
    }
}
