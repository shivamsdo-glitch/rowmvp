using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Parties;

/// <summary>
/// Who owns a parcel, read from its ownership interests (ADR 0023): the owner-role parties linked to the parcel itself and to its
/// parent tract. Used for the parcel's owner name, the owner-name search and the vested owner on Title and Closing.
/// </summary>
internal static class ParcelOwners
{
    /// <summary>Owner names by parcel (own links first, then the tract's), ignoring case when de-duplicating. <paramref name="ownerType"/> limits it to that Library owner type.</summary>
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> NamesAsync(
        IRowDbContext db, IReadOnlyCollection<Guid> parcelIds, string? ownerType, CancellationToken ct)
    {
        if (parcelIds.Count == 0) return new Dictionary<Guid, IReadOnlyList<string>>();
        var parcels = await db.Parcels.AsNoTracking().Where(p => parcelIds.Contains(p.Id)).Select(p => new { p.Id, p.ParentTractId }).ToListAsync(ct);
        var tractIds = parcels.Where(p => p.ParentTractId != null).Select(p => p.ParentTractId!.Value).Distinct().ToList();
        var links = (await db.PartyLinks.AsNoTracking()
                .Where(l => (l.ParcelId != null && parcelIds.Contains(l.ParcelId.Value)) || (l.ParentTractId != null && tractIds.Contains(l.ParentTractId.Value)))
                .ToListAsync(ct))
            .Where(l => PartyRoles.IsOwnerRole(l.Role) && (ownerType is null || l.OwnerType == ownerType))
            .ToList();
        var names = await db.Parties.AsNoTracking().Where(p => links.Select(l => l.PartyId).Distinct().Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var result = new Dictionary<Guid, IReadOnlyList<string>>();
        foreach (var parcel in parcels)
        {
            var mine = links.Where(l => l.ParcelId == parcel.Id).Concat(links.Where(l => l.ParcelId == null && l.ParentTractId == parcel.ParentTractId))
                .Where(l => names.ContainsKey(l.PartyId))
                .OrderBy(l => l.ParcelId == null).ThenBy(l => l.Role).ThenBy(l => names[l.PartyId], StringComparer.OrdinalIgnoreCase)
                .Select(l => names[l.PartyId]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (mine.Count > 0) result[parcel.Id] = mine;
        }
        return result;
    }

    /// <summary>The names joined as the screens show them, or null when the parcel has no owner recorded.</summary>
    public static async Task<string?> JoinedAsync(IRowDbContext db, Guid parcelId, string? ownerType, CancellationToken ct) =>
        (await NamesAsync(db, new[] { parcelId }, ownerType, ct)).TryGetValue(parcelId, out var names) ? string.Join("; ", names) : null;

    /// <summary>The parcels with an owner whose name contains the (lower-case) text, counting a tract's owners for each of its parcels.</summary>
    public static async Task<List<Guid>> ParcelIdsByOwnerAsync(IRowDbContext db, string term, CancellationToken ct)
    {
        var partyIds = db.Parties.AsNoTracking().Where(p => p.Name.ToLower().Contains(term) || (p.LegalName != null && p.LegalName.ToLower().Contains(term))).Select(p => p.Id);
        var links = (await db.PartyLinks.AsNoTracking().Where(l => partyIds.Contains(l.PartyId)).ToListAsync(ct)).Where(l => PartyRoles.IsOwnerRole(l.Role)).ToList();
        var direct = links.Where(l => l.ParcelId != null).Select(l => l.ParcelId!.Value).ToList();
        var tractIds = links.Where(l => l.ParentTractId != null).Select(l => l.ParentTractId!.Value).Distinct().ToList();
        var viaTract = tractIds.Count == 0 ? new List<Guid>() : await db.Parcels.AsNoTracking().Where(p => p.ParentTractId != null && tractIds.Contains(p.ParentTractId.Value)).Select(p => p.Id).ToListAsync(ct);
        return direct.Concat(viaTract).Distinct().ToList();
    }
}
