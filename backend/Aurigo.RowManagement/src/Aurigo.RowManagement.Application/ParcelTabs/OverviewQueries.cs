using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Parties;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.ParcelTabs;

// ── Across-parcel searches behind the Title & Survey, Appraisal and Negotiation & Closing menu items (Phase 1.5, ADR 0021) ──
// Each finds records by parcel number, parcel address, owner name or the record's own ID, so a person can open the right one and work on it.
// Each needs read access to the parcel tab it searches; changing a record goes through that tab's own endpoints and permissions.

public sealed record AppraisalOverviewRow(
    Guid Id, Guid ParcelId, string ParcelNumber, string? Address, string? OwnerName, string Number, string? AppraisalType, string? Appraiser, DateOnly? ReceivedDate,
    decimal AppraisalTotal, decimal? ApprovedJustCompensation);
public sealed record NegotiationOverviewRow(
    Guid Id, Guid ParcelId, string ParcelNumber, string? Address, string? OwnerName, string Number, string? AssignedTo, decimal? TotalAgreementAmount, string? JatResponse,
    DateOnly? PuaExecutedDate);
public sealed record TitleClosingOverviewRow(
    Guid Id, Guid ParcelId, string ParcelNumber, string? Address, string? OwnerName, string Number, string? TitleCompany, string? TitleClosingAgent,
    DateOnly? AnticipatedClosingDate, DateOnly? ActualClosingDate);
public sealed record EncumbranceOverviewRow(
    Guid Id, Guid ParcelId, string ParcelNumber, string? Address, string? OwnerName, string Number, string EncumbranceType, string DocumentTitle, string? CureStatus);
public sealed record EasementOverviewRow(
    Guid Id, Guid ParcelId, string ParcelNumber, string? Address, string? OwnerName, string Number, string? EasementType, DateOnly? StartDate, DateOnly? EndDate);

public sealed record AppraisalOverviewQuery(string? Search) : IRequest<IReadOnlyList<AppraisalOverviewRow>>;
public sealed record NegotiationOverviewQuery(string? Search) : IRequest<IReadOnlyList<NegotiationOverviewRow>>;
public sealed record TitleClosingOverviewQuery(string? Search) : IRequest<IReadOnlyList<TitleClosingOverviewRow>>;
public sealed record EncumbranceOverviewQuery(string? Search) : IRequest<IReadOnlyList<EncumbranceOverviewRow>>;
public sealed record EasementOverviewQuery(string? Search) : IRequest<IReadOnlyList<EasementOverviewRow>>;

public sealed class OverviewQueryHandlers :
    IRequestHandler<AppraisalOverviewQuery, IReadOnlyList<AppraisalOverviewRow>>,
    IRequestHandler<NegotiationOverviewQuery, IReadOnlyList<NegotiationOverviewRow>>,
    IRequestHandler<TitleClosingOverviewQuery, IReadOnlyList<TitleClosingOverviewRow>>,
    IRequestHandler<EncumbranceOverviewQuery, IReadOnlyList<EncumbranceOverviewRow>>,
    IRequestHandler<EasementOverviewQuery, IReadOnlyList<EasementOverviewRow>>
{
    /// <summary>The most rows one search returns; it is for finding a record, not exporting.</summary>
    public const int MaxRows = 200;
    public const int MaxSearchLength = 100;

    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public OverviewQueryHandlers(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    /// <summary>The parcels whose number, address or an owner's name contains the search text; null when there is no search text.</summary>
    private async Task<(string? Term, List<Guid>? ParcelIds)> MatchAsync(string? search, CancellationToken ct)
    {
        var term = search?.Trim().ToLower();
        if (string.IsNullOrEmpty(term)) return (null, null);
        if (term.Length > MaxSearchLength) term = term[..MaxSearchLength];
        var byParcel = _db.Parcels.AsNoTracking().Where(p => p.Number.ToLower().Contains(term) || (p.Address != null && p.Address.ToLower().Contains(term))).Select(p => p.Id);
        var byOwner = await ParcelOwners.ParcelIdsByOwnerAsync(_db, term, ct);
        return (term, (await byParcel.ToListAsync(ct)).Union(byOwner).ToList());
    }

    private sealed record ParcelFacts(string Number, string? Address, string? Owners);

    private async Task<IReadOnlyDictionary<Guid, ParcelFacts>> FactsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        var parcels = await _db.Parcels.AsNoTracking().Where(p => wanted.Contains(p.Id)).Select(p => new { p.Id, p.Number, p.Address }).ToListAsync(ct);
        var owners = (await ParcelOwners.NamesAsync(_db, wanted, null, ct)).ToDictionary(o => o.Key, o => string.Join("; ", o.Value));
        return parcels.ToDictionary(p => p.Id, p => new ParcelFacts(p.Number, p.Address, owners.GetValueOrDefault(p.Id)));
    }

    public async Task<IReadOnlyList<AppraisalOverviewRow>> Handle(AppraisalOverviewQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelAppraisal, PermissionAction.Read, ct);
        var (term, ids) = await MatchAsync(request.Search, ct);
        var q = _db.ParcelAppraisals.AsNoTracking().AsQueryable();
        if (term is not null) q = q.Where(a => ids!.Contains(a.ParcelId) || a.Number.ToLower().Contains(term) || (a.Appraiser != null && a.Appraiser.ToLower().Contains(term)));
        var rows = await q.OrderByDescending(a => a.CreatedAt).Take(MaxRows)
            .Select(a => new { a.Id, a.ParcelId, a.Number, a.AppraisalType, a.Appraiser, a.ReceivedDate, Total = a.Amounts.Sum(x => x.Amount), a.ApprovedJustCompensation }).ToListAsync(ct);
        var facts = await FactsAsync(rows.Select(r => r.ParcelId), ct);
        return rows.Select(r => { var f = facts.GetValueOrDefault(r.ParcelId); return new AppraisalOverviewRow(r.Id, r.ParcelId, f?.Number ?? "", f?.Address, f?.Owners, r.Number, r.AppraisalType, r.Appraiser, r.ReceivedDate, r.Total, r.ApprovedJustCompensation); }).ToList();
    }

    public async Task<IReadOnlyList<NegotiationOverviewRow>> Handle(NegotiationOverviewQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelNegotiation, PermissionAction.Read, ct);
        var (term, ids) = await MatchAsync(request.Search, ct);
        var q = _db.ParcelNegotiations.AsNoTracking().AsQueryable();
        if (term is not null) q = q.Where(n => ids!.Contains(n.ParcelId) || n.Number.ToLower().Contains(term) || (n.AssignedTo != null && n.AssignedTo.ToLower().Contains(term)));
        var rows = await q.OrderByDescending(n => n.CreatedAt).Take(MaxRows).ToListAsync(ct);
        var facts = await FactsAsync(rows.Select(r => r.ParcelId), ct);
        return rows.Select(n => { var f = facts.GetValueOrDefault(n.ParcelId); return new NegotiationOverviewRow(n.Id, n.ParcelId, f?.Number ?? "", f?.Address, f?.Owners, n.Number, n.AssignedTo, n.TotalAgreementAmount, n.JatResponse, n.PuaExecutedDate); }).ToList();
    }

    public async Task<IReadOnlyList<TitleClosingOverviewRow>> Handle(TitleClosingOverviewQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.TitleClosing, PermissionAction.Read, ct);
        var (term, ids) = await MatchAsync(request.Search, ct);
        var q = _db.ParcelTitleClosings.AsNoTracking().AsQueryable();
        if (term is not null) q = q.Where(t => ids!.Contains(t.ParcelId) || t.Number.ToLower().Contains(term) || (t.TitleCompany != null && t.TitleCompany.ToLower().Contains(term)));
        var rows = await q.OrderByDescending(t => t.CreatedAt).Take(MaxRows).ToListAsync(ct);
        var facts = await FactsAsync(rows.Select(r => r.ParcelId), ct);
        return rows.Select(t => { var f = facts.GetValueOrDefault(t.ParcelId); return new TitleClosingOverviewRow(t.Id, t.ParcelId, f?.Number ?? "", f?.Address, f?.Owners, t.Number, t.TitleCompany, t.TitleClosingAgent, t.AnticipatedClosingDate, t.ActualClosingDate); }).ToList();
    }

    public async Task<IReadOnlyList<EncumbranceOverviewRow>> Handle(EncumbranceOverviewQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.TitleEncumbrances, PermissionAction.Read, ct);
        var (term, ids) = await MatchAsync(request.Search, ct);
        var q = _db.ParcelEncumbrances.AsNoTracking().AsQueryable();
        if (term is not null) q = q.Where(e => ids!.Contains(e.ParcelId) || e.Number.ToLower().Contains(term) || e.DocumentTitle.ToLower().Contains(term));
        var rows = await q.OrderByDescending(e => e.CreatedAt).Take(MaxRows).ToListAsync(ct);
        var facts = await FactsAsync(rows.Select(r => r.ParcelId), ct);
        return rows.Select(e => { var f = facts.GetValueOrDefault(e.ParcelId); return new EncumbranceOverviewRow(e.Id, e.ParcelId, f?.Number ?? "", f?.Address, f?.Owners, e.Number, e.EncumbranceType, e.DocumentTitle, e.CureStatus); }).ToList();
    }

    public async Task<IReadOnlyList<EasementOverviewRow>> Handle(EasementOverviewQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Easements, PermissionAction.Read, ct);
        var (term, ids) = await MatchAsync(request.Search, ct);
        var q = _db.ParcelEasements.AsNoTracking().AsQueryable();
        if (term is not null) q = q.Where(e => ids!.Contains(e.ParcelId) || e.Number.ToLower().Contains(term));
        var rows = await q.OrderByDescending(e => e.CreatedAt).Take(MaxRows).ToListAsync(ct);
        var facts = await FactsAsync(rows.Select(r => r.ParcelId), ct);
        return rows.Select(e => { var f = facts.GetValueOrDefault(e.ParcelId); return new EasementOverviewRow(e.Id, e.ParcelId, f?.Number ?? "", f?.Address, f?.Owners, e.Number, e.EasementType, e.StartDate, e.EndDate); }).ToList();
    }
}
