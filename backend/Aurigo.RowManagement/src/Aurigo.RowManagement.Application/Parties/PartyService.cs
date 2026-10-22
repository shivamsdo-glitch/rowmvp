using FluentValidation;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Parties;

/// <summary>The editable details of a party, shared by creating one, editing one and adding one while linking (ROW-014).</summary>
public interface IPartyFields
{
    PartyKind Kind { get; }
    string Name { get; }
    string? LegalName { get; }
    string? Email { get; }
    string? Phone { get; }
    string? AddressLine1 { get; }
    string? AddressLine2 { get; }
    string? City { get; }
    string? State { get; }
    string? PostalCode { get; }
    string? Notes { get; }
    /// <summary>The party's addresses; null leaves the saved ones as they are, a list replaces them.</summary>
    IReadOnlyList<PartyAddressInput>? Addresses { get; }
    /// <summary>The party's contact people; null leaves the saved ones as they are, a list replaces them.</summary>
    IReadOnlyList<PartyContactInput>? Contacts { get; }
}

internal static class PartyRules
{
    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static void Apply(Party p, IPartyFields f)
    {
        p.Kind = f.Kind;
        p.Name = f.Name.Trim();
        p.LegalName = Clean(f.LegalName);
        p.Email = Clean(f.Email);
        p.Phone = Clean(f.Phone);
        p.AddressLine1 = Clean(f.AddressLine1);
        p.AddressLine2 = Clean(f.AddressLine2);
        p.City = Clean(f.City);
        p.State = Clean(f.State)?.ToUpperInvariant();
        p.PostalCode = Clean(f.PostalCode);
        p.Notes = Clean(f.Notes);
    }

    /// <summary>
    /// Replaces the party's addresses and contacts with the ones sent (when any are sent). The party must have been loaded with its
    /// children when it already exists, so the old rows can be removed.
    /// </summary>
    public static void ApplyChildren(IRowDbContext db, Party p, IPartyFields f)
    {
        if (f.Addresses is { } addresses)
        {
            db.PartyAddresses.RemoveRange(p.Addresses.ToList());
            p.Addresses = addresses.Select(a => new PartyAddress
            {
                PartyId = p.Id, AddressType = Clean(a.AddressType), Address = Clean(a.Address), ContactPerson = Clean(a.ContactPerson),
                City = Clean(a.City), State = Clean(a.State), Zip = Clean(a.Zip),
            }).ToList();
            db.PartyAddresses.AddRange(p.Addresses);
        }
        if (f.Contacts is { } contacts)
        {
            db.PartyContacts.RemoveRange(p.Contacts.ToList());
            p.Contacts = contacts.Select(c => new PartyContact
            {
                PartyId = p.Id, ContactType = Clean(c.ContactType), Name = c.Name.Trim(), Phone = Clean(c.Phone), Email = Clean(c.Email),
                Address = Clean(c.Address), IsActive = c.IsActive, IsPrimary = c.IsPrimary,
            }).ToList();
            db.PartyContacts.AddRange(p.Contacts);
        }
    }
}

/// <summary>The checks every way of entering a party shares.</summary>
public abstract class PartyFieldsValidator<T> : FluentValidation.AbstractValidator<T> where T : IPartyFields
{
    protected PartyFieldsValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(200);
        RuleFor(x => x.LegalName).MaximumLength(300);
        RuleFor(x => x.Email).EmailAddress().WithMessage("Enter a valid email address.").MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).Matches(@"^[\d\s().+\-xX#]{3,40}$").WithMessage("Enter a valid phone number.").When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.AddressLine1).MaximumLength(200);
        RuleFor(x => x.AddressLine2).MaximumLength(200);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.State).Matches(@"^[A-Za-z]{2}$").WithMessage("Use the two-letter state code.").When(x => !string.IsNullOrWhiteSpace(x.State));
        RuleFor(x => x.PostalCode).Matches(@"^[0-9A-Za-z \-]{3,10}$").WithMessage("Enter a valid ZIP or postal code.").When(x => !string.IsNullOrWhiteSpace(x.PostalCode));
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Addresses).Must(a => a is null || a.Count <= 50).WithMessage("A party can have at most 50 addresses.");
        RuleForEach(x => x.Addresses).ChildRules(a =>
        {
            a.RuleFor(i => i.AddressType).MaximumLength(100);
            a.RuleFor(i => i.Address).MaximumLength(300);
            a.RuleFor(i => i.ContactPerson).MaximumLength(200);
            a.RuleFor(i => i.City).MaximumLength(100);
            a.RuleFor(i => i.State).MaximumLength(50);
            a.RuleFor(i => i.Zip).MaximumLength(20);
        });
        RuleFor(x => x.Contacts).Must(c => c is null || c.Count <= 50).WithMessage("A party can have at most 50 contacts.");
        RuleFor(x => x.Contacts).Must(c => c is null || c.Count(i => i.IsPrimary) <= 1).WithMessage("Only one contact can be the primary contact.");
        RuleForEach(x => x.Contacts).ChildRules(c =>
        {
            c.RuleFor(i => i.Name).NotEmpty().WithMessage("Each contact needs a name.").MaximumLength(200);
            c.RuleFor(i => i.ContactType).MaximumLength(100);
            c.RuleFor(i => i.Phone).Matches(@"^[\d\s().+\-xX#]{3,40}$").WithMessage("Enter a valid phone number.").When(i => !string.IsNullOrWhiteSpace(i.Phone));
            c.RuleFor(i => i.Email).EmailAddress().WithMessage("Enter a valid email address.").MaximumLength(200).When(i => !string.IsNullOrWhiteSpace(i.Email));
            c.RuleFor(i => i.Address).MaximumLength(300);
        });
    }
}

/// <summary>Loading and shaping parties and their links (ROW-014..018). Scoped: one per request.</summary>
public sealed class PartyService
{
    private readonly IRowDbContext _db;

    public PartyService(IRowDbContext db) => _db = db;

    public async Task<Party> LoadPartyAsync(Guid id, CancellationToken ct) =>
        await _db.Parties.Include(p => p.Addresses).Include(p => p.Contacts).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Party", id);

    // ── shaping ──────────────────────────────────────────────────────────

    public static PartySummaryDto ToSummary(Party p, int linkCount) =>
        new(p.Id, p.Kind, p.Name, p.City, p.State, p.Phone, p.Email, p.TaxIdProtected is not null, linkCount);

    public static PartyDto ToDto(Party p, int linkCount) => new(
        p.Id, p.Kind, p.Name, p.LegalName, p.Email, p.Phone, p.AddressLine1, p.AddressLine2, p.City, p.State, p.PostalCode, p.Notes,
        p.TaxIdType, p.TaxIdType is { } type && p.TaxIdLast4 is { } last4 ? TaxId.Mask(type, last4) : null,
        p.TaxIdProtected is not null, p.W9ReceivedOn, p.Reportable1099, linkCount,
        p.Addresses.OrderBy(a => a.CreatedAt).ThenBy(a => a.Address).Select(a => new PartyAddressDto(a.Id, a.AddressType, a.Address, a.ContactPerson, a.City, a.State, a.Zip)).ToList(),
        p.Contacts.OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new PartyContactDto(c.Id, c.ContactType, c.Name, c.Phone, c.Email, c.Address, c.IsActive, c.IsPrimary)).ToList(),
        p.UpdatedAt);

    public async Task<IReadOnlyDictionary<Guid, int>> LinkCountsAsync(IReadOnlyCollection<Guid> partyIds, CancellationToken ct) =>
        await _db.PartyLinks.Where(l => partyIds.Contains(l.PartyId)).GroupBy(l => l.PartyId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public async Task<PartyDto> ToDtoAsync(Party p, CancellationToken ct) =>
        ToDto(p, await _db.PartyLinks.CountAsync(l => l.PartyId == p.Id, ct));

    /// <summary>The links as DTOs, each with its party, in a stable order.</summary>
    public async Task<IReadOnlyList<PartyLinkDto>> ToLinkDtosAsync(IReadOnlyList<PartyLink> links, CancellationToken ct)
    {
        if (links.Count == 0) return Array.Empty<PartyLinkDto>();
        var partyIds = links.Select(l => l.PartyId).Distinct().ToList();
        var parties = await _db.Parties.Where(p => partyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var counts = await LinkCountsAsync(partyIds, ct);
        var tractIds = links.Where(l => l.ParentTractId is not null).Select(l => l.ParentTractId!.Value).Distinct().ToList();
        var tracts = await _db.ParentTracts.Where(t => tractIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Number, ct);

        return links
            .Where(l => parties.ContainsKey(l.PartyId))
            .Select(l => new PartyLinkDto(
                l.Id, PartyRoles.CategoryOf(l.Role), l.Role, l.SharePercent, l.OwnerType, l.Notes, l.LeaseOnFile, l.LeaseDetails, l.InsuranceDetails,
                l.ParentTractId is not null ? "Tract" : "Parcel", l.ParentTractId, l.ParentTractId is { } t ? tracts.GetValueOrDefault(t) : null, l.ParcelId,
                ToSummary(parties[l.PartyId], counts.GetValueOrDefault(l.PartyId)), l.DisplacementRequired))
            .OrderBy(d => d.Role).ThenBy(d => d.Party.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Id)
            .ToList();
    }

    public static ParcelPartiesDto Group(IReadOnlyList<PartyLinkDto> links, Guid? tractId, string? tractNumber) => new(
        links.Where(l => l.Category == PartyCategory.Ownership).ToList(),
        links.Where(l => l.Category == PartyCategory.Contact).ToList(),
        links.Where(l => l.Category == PartyCategory.Occupant).ToList(),
        links.Where(l => l.Category == PartyCategory.Abutter).ToList(),
        tractId, tractNumber);

    /// <summary>Everyone around a parcel: what is recorded on the parcel itself and what it inherits from its parent tract.</summary>
    public async Task<ParcelPartiesDto> ForParcelAsync(Parcel parcel, CancellationToken ct)
    {
        var parcelId = parcel.Id;
        var tractId = parcel.ParentTractId;
        var links = await _db.PartyLinks
            .Where(l => l.ParcelId == parcelId || (tractId != null && l.ParentTractId == tractId))
            .ToListAsync(ct);
        var tractNumber = tractId is { } t ? await _db.ParentTracts.Where(x => x.Id == t).Select(x => x.Number).FirstOrDefaultAsync(ct) : null;
        return Group(await ToLinkDtosAsync(links, ct), tractId, tractNumber);
    }

    public async Task<ParcelPartiesDto> ForTractAsync(ParentTract tract, CancellationToken ct)
    {
        var links = await _db.PartyLinks.Where(l => l.ParentTractId == tract.Id).ToListAsync(ct);
        return Group(await ToLinkDtosAsync(links, ct), tract.Id, tract.Number);
    }

    // ── keeping a parcel's own links through split and combine ───────────

    /// <summary>
    /// The people around a parcel do not change because the parcel is split or combined, so new pieces take over the source
    /// parcels' own links (the tract's links reach them through the tract). When several sources have the same party in the
    /// same role, the piece gets it once.
    /// </summary>
    public async Task CarryLinksAsync(IReadOnlyCollection<Guid> sourceParcelIds, IReadOnlyCollection<Guid> newParcelIds, CancellationToken ct)
    {
        var sources = await _db.PartyLinks.Where(l => l.ParcelId != null && sourceParcelIds.Contains(l.ParcelId.Value)).ToListAsync(ct);
        var distinct = sources.GroupBy(l => (l.PartyId, l.Role)).Select(g => g.OrderBy(l => l.CreatedAt).First()).ToList();
        foreach (var parcelId in newParcelIds)
            foreach (var l in distinct)
                _db.PartyLinks.Add(new PartyLink
                {
                    PartyId = l.PartyId, ParcelId = parcelId, Role = l.Role, SharePercent = l.SharePercent, OwnerType = l.OwnerType, Notes = l.Notes,
                    LeaseOnFile = l.LeaseOnFile, LeaseDetails = l.LeaseDetails, InsuranceDetails = l.InsuranceDetails,
                });
    }
}
