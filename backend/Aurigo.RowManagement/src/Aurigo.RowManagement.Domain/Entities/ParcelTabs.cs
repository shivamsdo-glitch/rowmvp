using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// One logged contact about a parcel (Phase 1.5 Contact Log tab, ADR 0017). Unlike the negotiation diary (ROW-020) it can be changed,
/// has its own access (the Contact Log page) and is numbered "CL-&lt;parcel number&gt;-&lt;sequence&gt;".
/// </summary>
public class ParcelContactLogEntry : EntityBase
{
    public Guid ParcelId { get; set; }
    /// <summary>1, 2, 3... within the parcel: one more than the highest now on it, so removing an earlier entry never renumbers or reissues. With the parcel number it makes the displayed id.</summary>
    public int Sequence { get; set; }
    public string Number { get; set; } = default!;
    public DateTime ContactedAt { get; set; }
    /// <summary>A value of the Library's mode of contact list.</summary>
    public string? Mode { get; set; }
    public string? ContactedBy { get; set; }
    /// <summary>Who was spoken to, as written (usually one of the parcel's contacts).</summary>
    public string? ContactPerson { get; set; }
    public string? Notes { get; set; }
    public List<ParcelContactLogLink> Links { get; set; } = new();
}

/// <summary>A record in another module that a contact log entry refers to.</summary>
public class ParcelContactLogLink : EntityBase
{
    public Guid ContactLogEntryId { get; set; }
    public string RecordIdentifier { get; set; } = default!;
    public string? ModuleName { get; set; }
    public string? Notes { get; set; }
}

/// <summary>An easement on a parcel with its term and station range (Phase 1.5 Easements tab).</summary>
public class ParcelEasement : EntityBase
{
    public Guid ParcelId { get; set; }
    /// <summary>Entered by the user; unique within the parcel.</summary>
    public string Number { get; set; } = default!;
    /// <summary>A value of the Library's easement type list.</summary>
    public string? EasementType { get; set; }
    public string? Description { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? StartStation { get; set; }
    public string? EndStation { get; set; }
}
