using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Dashboard;

// ── The dashboard (Phase 1.5, ADR 0022): one per person, plus a roll-up for managers ───────────────────────────────────────────
// "Done" comes from the audit log (who changed what, when), so it needs no new bookkeeping. "Pending" is computed from the live records
// and only lists the work the person's role can open. Nothing is stored and nothing is emailed.

public sealed record DashboardPendingDto(string Key, string Label, int Count, string Link, string Tone);
public sealed record DashboardDayDto(DateOnly Date, int Count);
public sealed record DashboardKindDto(string Kind, int Created, int Updated, int Deleted);
public sealed record DashboardActionDto(string Kind, string Action, DateTime At);
public sealed record DashboardDoneDto(
    int WindowDays, int Created, int Updated, int Deleted, int Total, DateTime? LastActiveAt,
    IReadOnlyList<DashboardDayDto> Days, IReadOnlyList<DashboardKindDto> ByKind, IReadOnlyList<DashboardActionDto> Recent);

public sealed record MyDashboardDto(
    Guid UserId, string FullName, IReadOnlyList<string> Roles, bool IsManager, DateTime GeneratedAt,
    IReadOnlyList<DashboardPendingDto> Pending, DashboardDoneDto Done);

public sealed record TeamTileDto(string Key, string Label, int Value, string Link, string Tone);
public sealed record TeamStatusDto(string Status, string Color, int Parcels);
public sealed record TeamMemberDto(
    Guid UserId, string FullName, IReadOnlyList<string> Roles, int Created, int Updated, int Deleted, int Total, DateTime? LastActiveAt,
    int OpenRequests, int DraftReports);
public sealed record TeamDashboardDto(
    DateTime GeneratedAt, int WindowDays, IReadOnlyList<TeamTileDto> Tiles, IReadOnlyList<TeamStatusDto> ParcelsByStatus,
    IReadOnlyList<DashboardDayDto> Days, IReadOnlyList<DashboardKindDto> ByKind, IReadOnlyList<TeamMemberDto> Members);

internal static class DashboardShared
{
    public const int WindowDays = 30;
    public const int SoonDays = 30;
    /// <summary>The most audit rows read for one dashboard; the window is short, so this only bites an unusually busy agency.</summary>
    public const int MaxAuditRows = 20_000;

    public static bool IsManager(ICurrentUser user) => user.Roles.Any(r => r is RoleNames.ProjectManager or RoleNames.Admin);

    public static DashboardDoneDto ToDto(ActivityTotals t) => new(
        WindowDays, t.Created, t.Updated, t.Deleted, t.Total, t.LastActiveAt,
        t.Days.Select(d => new DashboardDayDto(d.Date, d.Count)).ToList(),
        t.ByKind.Select(k => new DashboardKindDto(k.Kind, k.Created, k.Updated, k.Deleted)).ToList(),
        t.Recent.Select(a => new DashboardActionDto(a.Kind, a.Action.ToString(), a.At)).ToList());

    public static async Task<List<ActivityEntry>> EntriesAsync(IRowDbContext db, DateTime since, Guid? user, CancellationToken ct)
    {
        var q = db.AuditLogs.AsNoTracking().Where(a => a.ChangedAt >= since);
        if (user is { } u) q = q.Where(a => a.ChangedBy == u);
        return await q.OrderByDescending(a => a.ChangedAt).Take(MaxAuditRows)
            .Select(a => new ActivityEntry(a.EntityType, a.EntityId, a.Action, a.ChangedAt)).ToListAsync(ct);
    }

    /// <summary>How many records of each kind of pending work exist, for the pages <paramref name="can"/> allows.</summary>
    public sealed class Counter
    {
        private readonly IRowDbContext _db;
        private readonly IMediator _mediator;
        private readonly Func<string, PermissionAction, bool> _can;
        private readonly DateOnly _today;
        private readonly Guid _me;

        public Counter(IRowDbContext db, IMediator mediator, Func<string, PermissionAction, bool> can, DateOnly today, Guid me)
        {
            _db = db;
            _mediator = mediator;
            _can = can;
            _today = today;
            _me = me;
        }

        private string LinkFor(string pageKey, string link, string fallback) => _can(pageKey, PermissionAction.Read) ? link : fallback;

        public async Task<IReadOnlyList<DashboardPendingDto>> PendingAsync(CancellationToken ct)
        {
            var items = new List<DashboardPendingDto>();
            void Add(string key, string label, int count, string link, string tone) => items.Add(new DashboardPendingDto(key, label, count, link, tone));

            if (_can(PageKeys.Approvals, PermissionAction.Update))
            {
                var n = await _db.AcquisitionRequests.CountAsync(r => r.Status == AcquisitionRequestStatus.Submitted && r.CreatedBy != _me, ct);
                Add("requests-to-decide", "Acquisition requests waiting for your decision", n, "/requests", "warn");
                var reports = await _db.ParcelStaffReports.CountAsync(r => r.Status == ReportStatus.Submitted && r.SubmittedBy != _me, ct);
                Add("reports-to-decide", "Staff reports waiting for your decision", reports, "/parcels", "warn");
            }
            if (_can(PageKeys.Approvals, PermissionAction.Read))
            {
                var n = await _db.AcquisitionRequests.CountAsync(r => r.Status == AcquisitionRequestStatus.Submitted && r.CreatedBy == _me, ct);
                Add("my-requests", "Your acquisition requests still waiting for a decision", n, "/requests", "info");
            }
            if (_can(PageKeys.Parcels, PermissionAction.Read))
            {
                var drafts = await _db.ParcelStaffReports.CountAsync(r => r.Status == ReportStatus.Draft && r.CreatedBy == _me, ct);
                Add("my-draft-reports", "Your staff reports still in draft", drafts, "/parcels", "info");

                var reminders = await _mediator.Send(new ListRemindersQuery(7), ct);
                Add("deadlines-overdue", "Parcel deadlines that are overdue", reminders.Count(r => r.Status == nameof(DeadlineStatus.Overdue)), "/parcels", "danger");
                Add("deadlines-soon", "Parcel deadlines due within 7 days", reminders.Count(r => r.Status != nameof(DeadlineStatus.Overdue)), "/parcels", "warn");

                var soon = _today.AddDays(SoonDays);
                var roe = await _db.RightsOfEntry.CountAsync(r => r.Status == RightOfEntryStatus.Granted && r.ExpiresOn != null && r.ExpiresOn >= _today && r.ExpiresOn <= soon, ct);
                Add("roe-expiring", $"Rights of entry expiring within {SoonDays} days", roe, "/rights-of-entry", "warn");
            }
            if (_can(PageKeys.ParcelAppraisal, PermissionAction.Read))
            {
                var n = await _db.ParcelAppraisals.CountAsync(a => a.ApprovedJustCompensation == null, ct);
                Add("appraisals-open", "Appraisals without an approved just compensation", n, LinkFor(PageKeys.Appraisals, "/appraisals", "/parcels"), "warn");
            }
            if (_can(PageKeys.TitleClosing, PermissionAction.Read))
            {
                var soon = _today.AddDays(SoonDays);
                var n = await _db.ParcelTitleClosings.CountAsync(t => t.ActualClosingDate == null && t.AnticipatedClosingDate != null && t.AnticipatedClosingDate <= soon, ct);
                Add("closings-due", $"Closings due within {SoonDays} days, or past due", n, LinkFor(PageKeys.Negotiations, "/negotiations", "/parcels"), "warn");
            }
            if (_can(PageKeys.TitleEncumbrances, PermissionAction.Read))
            {
                var n = await _db.ParcelEncumbrances.CountAsync(e => e.CureStatus == null || e.CureStatus == "", ct);
                Add("encumbrances-open", "Title encumbrances with no cure status", n, LinkFor(PageKeys.Title, "/title", "/parcels"), "info");
            }
            return items;
        }
    }
}

// ── My dashboard ────────────────────────────────────────────────────────────

public sealed record GetMyDashboardQuery : IRequest<MyDashboardDto>;

public sealed class GetMyDashboardQueryHandler : IRequestHandler<GetMyDashboardQuery, MyDashboardDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentUser _user;
    private readonly IMediator _mediator;
    private readonly TimeProvider _clock;

    public GetMyDashboardQueryHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentUser user, IMediator mediator, TimeProvider clock)
    {
        _db = db;
        _permissions = permissions;
        _user = user;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task<MyDashboardDto> Handle(GetMyDashboardQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Dashboard, PermissionAction.Read, ct);
        var now = _clock.GetUtcNow().UtcDateTime;
        var grants = (await _permissions.GetEffectiveAsync(ct)).ToDictionary(g => g.PageKey, StringComparer.Ordinal);
        bool Can(string page, PermissionAction a) => grants.TryGetValue(page, out var g) && a switch
        {
            PermissionAction.Create => g.CanCreate, PermissionAction.Read => g.CanRead, PermissionAction.Update => g.CanUpdate, _ => g.CanDelete,
        };

        var pending = await new DashboardShared.Counter(_db, _mediator, Can, DateOnly.FromDateTime(now), _user.Id).PendingAsync(ct);
        var entries = await DashboardShared.EntriesAsync(_db, now.AddDays(-DashboardShared.WindowDays), _user.Id, ct);
        var done = ActivitySummary.Summarize(entries, now, DashboardShared.WindowDays);
        var name = await _db.AppUsers.AsNoTracking().Where(u => u.Id == _user.Id).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct) ?? _user.Email;
        return new MyDashboardDto(_user.Id, name.Trim(), _user.Roles, DashboardShared.IsManager(_user), now, pending, DashboardShared.ToDto(done));
    }
}

// ── The manager's roll-up ───────────────────────────────────────────────────

/// <summary>Everyone's work added together. Project managers and administrators only.</summary>
public sealed record GetTeamDashboardQuery : IRequest<TeamDashboardDto>;

public sealed class GetTeamDashboardQueryHandler : IRequestHandler<GetTeamDashboardQuery, TeamDashboardDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentUser _user;
    private readonly IMediator _mediator;
    private readonly TimeProvider _clock;

    public GetTeamDashboardQueryHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentUser user, IMediator mediator, TimeProvider clock)
    {
        _db = db;
        _permissions = permissions;
        _user = user;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task<TeamDashboardDto> Handle(GetTeamDashboardQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Dashboard, PermissionAction.Read, ct);
        if (!DashboardShared.IsManager(_user)) throw new ForbiddenException("The team view is for project managers and administrators.");

        var now = _clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var soon = today.AddDays(DashboardShared.SoonDays);

        // Tiles: the portfolio at a glance.
        var projects = await _db.Projects.CountAsync(ct);
        var activeParcels = await _db.Parcels.CountAsync(p => p.State == ParcelState.Active, ct);
        var requests = await _db.AcquisitionRequests.CountAsync(r => r.Status == AcquisitionRequestStatus.Submitted, ct);
        var reports = await _db.ParcelStaffReports.CountAsync(r => r.Status == ReportStatus.Submitted, ct);
        var reminders = await _mediator.Send(new ListRemindersQuery(7), ct);
        var roe = await _db.RightsOfEntry.CountAsync(r => r.Status == RightOfEntryStatus.Granted && r.ExpiresOn != null && r.ExpiresOn >= today && r.ExpiresOn <= soon, ct);
        var closings = await _db.ParcelTitleClosings.CountAsync(t => t.ActualClosingDate == null && t.AnticipatedClosingDate != null && t.AnticipatedClosingDate <= soon, ct);
        var tiles = new List<TeamTileDto>
        {
            new("projects", "Projects", projects, "/projects", "info"),
            new("parcels", "Active parcels", activeParcels, "/parcels", "info"),
            new("awaiting-decision", "Awaiting a decision", requests + reports, "/requests", "warn"),
            new("deadlines-overdue", "Overdue deadlines", reminders.Count(r => r.Status == nameof(DeadlineStatus.Overdue)), "/parcels", "danger"),
            new("roe-expiring", "Rights of entry expiring", roe, "/rights-of-entry", "warn"),
            new("closings-due", "Closings due or past due", closings, "/negotiations", "warn"),
        };

        // Parcels by status, in the agency's own order.
        var statuses = await _db.ParcelStatuses.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var counts = await _db.Parcels.Where(p => p.State == ParcelState.Active).GroupBy(p => p.StatusId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var byStatus = statuses.Select(s => new TeamStatusDto(s.Name, s.Color, counts.GetValueOrDefault(s.Id))).Where(s => s.Parcels > 0).ToList();

        // Each person's work, from the audit log, plus what they have waiting.
        var entries = await _db.AuditLogs.AsNoTracking().Where(a => a.ChangedAt >= now.AddDays(-DashboardShared.WindowDays))
            .OrderByDescending(a => a.ChangedAt).Take(DashboardShared.MaxAuditRows)
            .Select(a => new { a.ChangedBy, Entry = new ActivityEntry(a.EntityType, a.EntityId, a.Action, a.ChangedAt) }).ToListAsync(ct);
        var perUser = entries.GroupBy(e => e.ChangedBy).ToDictionary(g => g.Key, g => g.Select(e => e.Entry).ToList());
        var users = await _db.AppUsers.AsNoTracking().Where(u => u.IsActive).Select(u => new { u.Id, u.FirstName, u.LastName }).ToListAsync(ct);
        var roles = (await _db.UserRoles.AsNoTracking().Select(ur => new { ur.UserId, ur.Role!.Name }).ToListAsync(ct))
            .GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToList());
        var openRequests = await _db.AcquisitionRequests.Where(r => r.Status == AcquisitionRequestStatus.Submitted).GroupBy(r => r.CreatedBy).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var drafts = await _db.ParcelStaffReports.Where(r => r.Status == ReportStatus.Draft).GroupBy(r => r.CreatedBy).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);

        var members = users.Select(u =>
        {
            var t = ActivitySummary.Summarize(perUser.GetValueOrDefault(u.Id) ?? new List<ActivityEntry>(), now, DashboardShared.WindowDays);
            return new TeamMemberDto(u.Id, $"{u.FirstName} {u.LastName}".Trim(), roles.GetValueOrDefault(u.Id) ?? Array.Empty<string>(),
                t.Created, t.Updated, t.Deleted, t.Total, t.LastActiveAt, openRequests.GetValueOrDefault(u.Id), drafts.GetValueOrDefault(u.Id));
        }).OrderByDescending(m => m.Total).ThenBy(m => m.FullName, StringComparer.Ordinal).ToList();

        var all = ActivitySummary.Summarize(entries.Select(e => e.Entry), now, DashboardShared.WindowDays);
        return new TeamDashboardDto(now, DashboardShared.WindowDays, tiles, byStatus,
            all.Days.Select(d => new DashboardDayDto(d.Date, d.Count)).ToList(),
            all.ByKind.Select(k => new DashboardKindDto(k.Kind, k.Created, k.Updated, k.Deleted)).ToList(), members);
    }
}
