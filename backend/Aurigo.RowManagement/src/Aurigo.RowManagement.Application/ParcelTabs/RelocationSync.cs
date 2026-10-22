using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.ParcelTabs;

public sealed record ParcelRelocationProgressDto(
    int DisplacedInterests, int CompletedInterests, bool ReadyToAcquire, bool AcquiredStatusAvailable, string StatusName, bool IsAcquired);

/// <summary>Keeps a parcel's status in step with its relocations (ADR 0026).</summary>
public static class RelocationSync
{
    /// <summary>The ownership interests of a parcel that must be relocated: its own links and its parent tract's.</summary>
    public static async Task<List<Guid>> DisplacedInterestIdsAsync(IRowDbContext db, Parcel parcel, CancellationToken ct)
    {
        var tractId = parcel.ParentTractId;
        var links = await db.PartyLinks.AsNoTracking()
            .Where(l => l.DisplacementRequired && (l.ParcelId == parcel.Id || (tractId != null && l.ParentTractId == tractId)))
            .Select(l => new { l.Id, l.Role }).ToListAsync(ct);
        return links.Where(l => PartyRoles.CategoryOf(l.Role) == PartyCategory.Ownership).Select(l => l.Id).ToList();
    }

    public static async Task<ParcelRelocationProgressDto> ProgressAsync(IRowDbContext db, Parcel parcel, CancellationToken ct)
    {
        var interests = await DisplacedInterestIdsAsync(db, parcel, ct);
        var rows = await db.ParcelRelocations.AsNoTracking().Where(r => r.ParcelId == parcel.Id).Select(r => new RelocationProgressInput(r.PartyLinkId, r.RelocationComplete)).ToListAsync(ct);
        var progress = RelocationCalc.Progress(interests, rows);
        var acquired = await db.ParcelStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.Name == DefaultParcelRules.AcquiredStatus && s.IsActive, ct);
        var current = await db.ParcelStatuses.AsNoTracking().Where(s => s.Id == parcel.StatusId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "Unknown";
        return new(progress.Flagged, progress.Completed, progress.AllComplete, acquired is not null, current, current == DefaultParcelRules.AcquiredStatus);
    }

    /// <summary>
    /// When every displaced interest on the parcel has a completed relocation, moves the parcel to Acquired. Does nothing when the parcel has no displaced
    /// interests, is already Acquired, or the agency has no active "Acquired" status. It never moves a parcel back. Saves when it changes the parcel.
    /// </summary>
    public static async Task<bool> RunAsync(IRowDbContext db, Guid parcelId, CancellationToken ct)
    {
        var parcel = await db.Parcels.FirstOrDefaultAsync(p => p.Id == parcelId, ct);
        if (parcel is null) return false;
        var progress = await ProgressAsync(db, parcel, ct);
        if (!progress.ReadyToAcquire || progress.IsAcquired || !progress.AcquiredStatusAvailable) return false;
        var acquired = await db.ParcelStatuses.FirstAsync(s => s.Name == DefaultParcelRules.AcquiredStatus && s.IsActive, ct);
        parcel.StatusId = acquired.Id;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
