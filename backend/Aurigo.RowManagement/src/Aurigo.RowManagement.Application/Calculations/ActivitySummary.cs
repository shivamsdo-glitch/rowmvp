using System.Text;
using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Application.Calculations;

/// <summary>One audited change: what kind of record, which one, what happened and when (UTC). Taken from the audit log.</summary>
public sealed record ActivityEntry(string EntityType, Guid EntityId, AuditAction Action, DateTime ChangedAt);

public sealed record ActivityDay(DateOnly Date, int Count);
public sealed record ActivityKind(string Kind, int Created, int Updated, int Deleted);
public sealed record ActivityItem(string Kind, Guid EntityId, AuditAction Action, DateTime At);
public sealed record ActivityTotals(
    int Created, int Updated, int Deleted, IReadOnlyList<ActivityDay> Days, IReadOnlyList<ActivityKind> ByKind, IReadOnlyList<ActivityItem> Recent, DateTime? LastActiveAt)
{
    public int Total => Created + Updated + Deleted;
}

/// <summary>
/// Turns a person's audit rows into "what they did" for the dashboard. A save that changes five fields writes five audit rows, so rows are
/// collapsed: one action is one record and one kind of change, with the rows no more than a minute apart. Bookkeeping records nobody asked for (sign-in tokens, saved
/// filters, the project-number counter) are left out. Stateless: the caller passes <c>now</c>.
/// </summary>
public static class ActivitySummary
{
    /// <summary>Record types that change as a side effect of using the app, not as work the person did.</summary>
    public static readonly IReadOnlySet<string> IgnoredTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "UserToken", "SavedFilterProfile", "ProjectNumberCounter", "AppUser", "UserRole",
    };

    /// <summary>Audit rows of one record and kind of change this close together are one save, not several.</summary>
    public static readonly TimeSpan SameSaveGap = TimeSpan.FromSeconds(60);

    /// <summary>"ParcelAppraisalAmount" becomes "Parcel appraisal amount".</summary>
    public static string Label(string entityType)
    {
        var sb = new StringBuilder(entityType.Length + 4);
        for (var i = 0; i < entityType.Length; i++)
        {
            var c = entityType[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(entityType[i - 1])) sb.Append(' ');
            sb.Append(i == 0 ? c : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <param name="entries">The person's audit rows (any order, any age).</param>
    /// <param name="nowUtc">The current time; the window ends here.</param>
    /// <param name="windowDays">How far back "done" reaches (the totals and the list of kinds).</param>
    /// <param name="chartDays">How many days the per-day series covers, ending today.</param>
    /// <param name="recentCount">How many of the latest actions to list.</param>
    public static ActivityTotals Summarize(IEnumerable<ActivityEntry> entries, DateTime nowUtc, int windowDays = 30, int chartDays = 7, int recentCount = 10)
    {
        var since = nowUtc.AddDays(-windowDays);
        var actions = new List<ActivityItem>();
        foreach (var g in entries
                     .Where(e => e.ChangedAt >= since && e.ChangedAt <= nowUtc && !IgnoredTypes.Contains(e.EntityType))
                     .GroupBy(e => (e.EntityType, e.EntityId, e.Action)))
        {
            // Rows of one save are written together; a pause longer than SameSaveGap starts a new action.
            DateTime? last = null;
            foreach (var at in g.Select(x => x.ChangedAt).OrderBy(t => t))
            {
                if (last is { } l && at - l <= SameSaveGap) { actions[^1] = actions[^1] with { At = at }; }
                else actions.Add(new ActivityItem(Label(g.Key.EntityType), g.Key.EntityId, g.Key.Action, at));
                last = at;
            }
        }

        var today = DateOnly.FromDateTime(nowUtc);
        var days = Enumerable.Range(0, chartDays).Select(i => today.AddDays(i - (chartDays - 1)))
            .Select(d => new ActivityDay(d, actions.Count(a => DateOnly.FromDateTime(a.At) == d))).ToList();

        var byKind = actions.GroupBy(a => a.Kind)
            .Select(g => new ActivityKind(g.Key, g.Count(a => a.Action == AuditAction.Create), g.Count(a => a.Action == AuditAction.Update), g.Count(a => a.Action == AuditAction.Delete)))
            .OrderByDescending(k => k.Created + k.Updated + k.Deleted).ThenBy(k => k.Kind, StringComparer.Ordinal).ToList();

        var recent = actions.OrderByDescending(a => a.At).ThenBy(a => a.Kind, StringComparer.Ordinal).Take(recentCount).ToList();
        return new ActivityTotals(
            actions.Count(a => a.Action == AuditAction.Create), actions.Count(a => a.Action == AuditAction.Update), actions.Count(a => a.Action == AuditAction.Delete),
            days, byKind, recent, actions.Count == 0 ? null : actions.Max(a => a.At));
    }
}
