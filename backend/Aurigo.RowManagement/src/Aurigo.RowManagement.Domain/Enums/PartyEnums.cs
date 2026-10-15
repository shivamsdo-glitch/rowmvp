namespace Aurigo.RowManagement.Domain.Enums;

public enum PartyKind
{
    Person = 1,
    Organization = 2,
}

/// <summary>Which kind of tax identification number a party gave (ROW-018). The number itself is never stored in the clear.</summary>
public enum TaxIdType
{
    /// <summary>Social Security Number.</summary>
    Ssn = 1,
    /// <summary>Employer Identification Number.</summary>
    Ein = 2,
    /// <summary>Individual Taxpayer Identification Number.</summary>
    Itin = 3,
}

/// <summary>The four lists of people around a parcel (ROW-014..017).</summary>
public enum PartyCategory
{
    /// <summary>Owners and everyone else with an interest in the land (ROW-014).</summary>
    Ownership = 1,
    /// <summary>Attorneys, banks, designees and other people to deal with (ROW-015).</summary>
    Contact = 2,
    /// <summary>Tenants and occupants (ROW-016).</summary>
    Occupant = 3,
    /// <summary>Abutters and other interested parties, for notices (ROW-017).</summary>
    Abutter = 4,
}

/// <summary>How a party relates to a tract or parcel. Each role belongs to exactly one <see cref="PartyCategory"/>.</summary>
public enum PartyRole
{
    // Ownership (ROW-014)
    FeeOwner = 1,
    EasementHolder = 2,
    Lienholder = 3,
    Trustee = 4,
    LifeEstateHolder = 5,
    OtherInterest = 6,

    // Contact (ROW-015)
    Attorney = 20,
    Bank = 21,
    OwnerDesignee = 22,
    OwnerRepresentative = 23,
    OtherContact = 24,

    // Occupant (ROW-016)
    Tenant = 40,
    Occupant = 41,

    // Abutter (ROW-017)
    Abutter = 60,
    InterestedParty = 61,
}
