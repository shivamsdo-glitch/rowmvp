using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// An encumbrance found in the title research of a parcel (Phase 1.5 Title Encumbrances tab, ADR 0018): a lien, easement, covenant or other
/// burden, with the document it comes from, the exception to title and how it is to be cured. Numbered "ENC-&lt;parcel number&gt;-&lt;sequence&gt;".
/// </summary>
public class ParcelEncumbrance : EntityBase
{
    public Guid ParcelId { get; set; }
    /// <summary>1, 2, 3... within the parcel: one more than the highest now on it.</summary>
    public int Sequence { get; set; }
    public string Number { get; set; } = default!;
    /// <summary>A value of the Library's encumbrance type list.</summary>
    public string EncumbranceType { get; set; } = default!;
    public string DocumentTitle { get; set; } = default!;
    public DateOnly? DocumentDate { get; set; }
    public DateOnly? DateRecorded { get; set; }
    /// <summary>The document number, volume and page.</summary>
    public string? DocumentNumber { get; set; }
    public bool IsUnrecorded { get; set; }
    public string Grantor { get; set; } = default!;
    public string Grantee { get; set; } = default!;
    public DateOnly? ExceptionSubmittedDate { get; set; }
    public DateOnly? ExceptionApprovedDate { get; set; }
    public string? ImpactAnalysis { get; set; }
    public string? RecommendedCure { get; set; }
    /// <summary>A value of the Library's cure status list.</summary>
    public string? CureStatus { get; set; }
    public string? ReviewNotes { get; set; }
    public List<ParcelEncumbranceLink> Links { get; set; } = new();
}

/// <summary>A record in another module that an encumbrance refers to.</summary>
public class ParcelEncumbranceLink : EntityBase
{
    public Guid EncumbranceId { get; set; }
    public string RecordIdentifier { get; set; } = default!;
    public string? ModuleName { get; set; }
    public string? Notes { get; set; }
}
