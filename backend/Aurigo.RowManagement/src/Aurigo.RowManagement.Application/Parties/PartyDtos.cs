using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Application.Parties;

/// <summary>A party as shown in lists and pickers. Never carries tax details.</summary>
public sealed record PartySummaryDto(
    Guid Id, PartyKind Kind, string Name, string? City, string? State, string? Phone, string? Email, bool HasTaxId, int LinkCount);

/// <summary>
/// A party in full. The tax number is never returned: <see cref="TaxIdMasked"/> shows the last four digits only
/// (for example <c>***-**-1234</c>).
/// </summary>
public sealed record PartyDto(
    Guid Id, PartyKind Kind, string Name, string? LegalName, string? Email, string? Phone,
    string? AddressLine1, string? AddressLine2, string? City, string? State, string? PostalCode, string? Notes,
    TaxIdType? TaxIdType, string? TaxIdMasked, bool HasTaxId, DateOnly? W9ReceivedOn, bool Reportable1099,
    int LinkCount, IReadOnlyList<PartyAddressDto> Addresses, IReadOnlyList<PartyContactDto> Contacts, DateTime UpdatedAt);

public sealed record PartyAddressInput(string? AddressType, string? Address, string? ContactPerson, string? City, string? State, string? Zip);
public sealed record PartyContactInput(string? ContactType, string Name, string? Phone, string? Email, string? Address, bool IsActive, bool IsPrimary);
public sealed record PartyAddressDto(Guid Id, string? AddressType, string? Address, string? ContactPerson, string? City, string? State, string? Zip);
public sealed record PartyContactDto(Guid Id, string? ContactType, string Name, string? Phone, string? Email, string? Address, bool IsActive, bool IsPrimary);

/// <summary>One party's relationship to a tract or parcel. <see cref="Source"/> is "Parcel" or "Tract" (a parcel also sees its tract's).</summary>
public sealed record PartyLinkDto(
    Guid Id, PartyCategory Category, PartyRole Role, decimal? SharePercent, string? OwnerType, string? Notes,
    bool LeaseOnFile, string? LeaseDetails, string? InsuranceDetails,
    string Source, Guid? ParentTractId, string? TractNumber, Guid? ParcelId, PartySummaryDto Party);

/// <summary>Everyone around a parcel, in the four lists of ROW-014..017.</summary>
public sealed record ParcelPartiesDto(
    IReadOnlyList<PartyLinkDto> Ownership, IReadOnlyList<PartyLinkDto> Contacts,
    IReadOnlyList<PartyLinkDto> Occupants, IReadOnlyList<PartyLinkDto> Abutters,
    /// <summary>The parcel's parent tract, where its owners are recorded; null when it has none.</summary>
    Guid? ParentTractId, string? TractNumber);

/// <summary>Where a party appears, for the party's own page.</summary>
public sealed record PartyUsageDto(
    Guid LinkId, PartyRole Role, PartyCategory Category, string TargetKind, Guid TargetId, string TargetLabel,
    Guid ProjectId, string ProjectNumber);
