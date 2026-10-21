using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// A person or organization the agency deals with: an owner, lienholder, tenant, attorney, bank, abutter and so on
/// (ROW-014..018). The record holds who they are and how to reach them; how each one relates to a tract or parcel is a
/// <see cref="PartyLink"/>, so the same person is entered once however many parcels they appear on.
/// </summary>
public class Party : EntityBase
{
    public PartyKind Kind { get; set; } = PartyKind.Person;
    /// <summary>The name used on letters and screens.</summary>
    public string Name { get; set; } = default!;
    /// <summary>The name exactly as it appears on the vesting deed, when that differs.</summary>
    public string? LegalName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    /// <summary>Two-letter state or territory code.</summary>
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Notes { get; set; }

    // ── tax information for payees (ROW-018) ──
    public TaxIdType? TaxIdType { get; set; }
    /// <summary>The tax number, encrypted. Never returned by the API and never written to the audit log.</summary>
    public string? TaxIdProtected { get; set; }
    /// <summary>The last four digits, the only part shown to anyone.</summary>
    public string? TaxIdLast4 { get; set; }
    /// <summary>When a signed W-9 was received.</summary>
    public DateOnly? W9ReceivedOn { get; set; }
    /// <summary>The party is paid by the agency and is reported on a 1099.</summary>
    public bool Reportable1099 { get; set; }

    // ── addresses and contact people (ADR 0023: the Ownership interests tab) ──
    public List<PartyAddress> Addresses { get; set; } = new();
    public List<PartyContact> Contacts { get; set; } = new();
}

/// <summary>One mailing or physical address of a party, with its type from the Library's Address type list.</summary>
public class PartyAddress : EntityBase
{
    public Guid PartyId { get; set; }
    public string? AddressType { get; set; }
    public string? Address { get; set; }
    public string? ContactPerson { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
}

/// <summary>A person to deal with for a party (a spouse, an agent, a family contact), with the Library's contact type.</summary>
public class PartyContact : EntityBase
{
    public Guid PartyId { get; set; }
    public string? ContactType { get; set; }
    public string Name { get; set; } = default!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPrimary { get; set; }
}

/// <summary>
/// How a party relates to one tract or one parcel: an owner with a share, a lienholder, an attorney to contact, a tenant, an
/// abutter to notify (ROW-014..017). Ownership is recorded on the parent tract when the parcel has one (the tract of record);
/// a parcel sees its tract's links as well as its own.
/// </summary>
public class PartyLink : EntityBase
{
    public Guid PartyId { get; set; }
    /// <summary>Exactly one of <see cref="ParentTractId"/> and <see cref="ParcelId"/> is set.</summary>
    public Guid? ParentTractId { get; set; }
    public Guid? ParcelId { get; set; }
    public PartyRole Role { get; set; }
    /// <summary>The party's share of the interest as a percentage (0 to 100), for roles that hold a share.</summary>
    public decimal? SharePercent { get; set; }
    /// <summary>For an ownership interest: a value of the Library's Owner type list ("Vested Owner", "Prior Owner"...). It describes this party on this parcel.</summary>
    public string? OwnerType { get; set; }
    public string? Notes { get; set; }

    // ── tenants (ROW-016) ──
    public bool LeaseOnFile { get; set; }
    public string? LeaseDetails { get; set; }
    public string? InsuranceDetails { get; set; }
}
