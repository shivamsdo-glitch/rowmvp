using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Application.Calculations;

public sealed record RollupParcel(Guid StatusId, Guid? GroupId, decimal AreaSqFt, ParcelState State);

/// <summary><see cref="Key"/> is the status id, or the group id (null = parcels with no group).</summary>
public sealed record RollupBucket(Guid? Key, int Count, decimal AreaSqFt);

public sealed record RollupResult(int TotalCount, decimal TotalAreaSqFt, IReadOnlyList<RollupBucket> ByStatus, IReadOnlyList<RollupBucket> ByGroup);

/// <summary>Pure roll-up of parcel counts and areas under a project or corridor (ROW-006). Only active parcels count.</summary>
public static class ParcelRollup
{
    public const decimal SqFtPerAcre = 43_560m;

    public static decimal ToAcres(decimal sqFt) => Math.Round(sqFt / SqFtPerAcre, 4);

    /// <summary>
    /// Totals and per-status / per-group buckets. Every id in <paramref name="statusIds"/> and <paramref name="groupIds"/> gets a
    /// bucket (zero if empty) in the given order, so screens show empty columns; a "no group" bucket is added when needed.
    /// </summary>
    public static RollupResult Summarize(IEnumerable<RollupParcel> parcels, IReadOnlyList<Guid> statusIds, IReadOnlyList<Guid> groupIds)
    {
        var active = parcels.Where(p => p.State == ParcelState.Active).ToList();

        var byStatus = statusIds.Select(id => Bucket(id, active.Where(p => p.StatusId == id))).ToList();
        // A parcel can still carry a status that was later removed from the list; keep its numbers visible.
        foreach (var orphan in active.Select(p => p.StatusId).Distinct().Where(id => !statusIds.Contains(id)))
            byStatus.Add(Bucket(orphan, active.Where(p => p.StatusId == orphan)));

        var byGroup = groupIds.Select(id => Bucket(id, active.Where(p => p.GroupId == id))).ToList();
        var ungrouped = active.Where(p => p.GroupId is null || !groupIds.Contains(p.GroupId.Value)).ToList();
        if (ungrouped.Count > 0) byGroup.Add(Bucket(null, ungrouped));

        return new RollupResult(active.Count, active.Sum(p => p.AreaSqFt), byStatus, byGroup);
    }

    private static RollupBucket Bucket(Guid? key, IEnumerable<RollupParcel> parcels)
    {
        var list = parcels.ToList();
        return new RollupBucket(key, list.Count, list.Sum(p => p.AreaSqFt));
    }
}
