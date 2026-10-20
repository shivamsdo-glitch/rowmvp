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

/// <summary>
/// The title and closing record of a parcel (Phase 1.5 Title and Closing tab, ADR 0018): one per parcel, numbered "TC-&lt;parcel number&gt;".
/// It holds who is handling title and closing, the purchase and sale agreement and closing dates, and two grids: the conveyance documents
/// and the title commitments.
/// </summary>
public class ParcelTitleClosing : EntityBase
{
    public Guid ParcelId { get; set; }
    public string Number { get; set; } = default!;
    public string? AssignedTo { get; set; }
    /// <summary>A value of the Library's title company list.</summary>
    public string? TitleCompany { get; set; }
    public string? TitleClosingAgent { get; set; }
    public string? GfNumber { get; set; }
    public string? FinalTitlePolicyNo { get; set; }

    public DateOnly? PsaSignedByOwnerDate { get; set; }
    public DateOnly? PsaSignedByAtpDate { get; set; }
    public decimal? PsaAmount { get; set; }
    public DateOnly? SettlementReceivedDate { get; set; }
    public DateOnly? SettlementExecutedDate { get; set; }
    public bool CurativeItemsCompleted { get; set; }
    public DateOnly? ClosingPackageSubmittedDate { get; set; }
    public DateOnly? ClosingPackageApprovedDate { get; set; }
    public DateOnly? ClosingInstructionDate { get; set; }
    public DateOnly? AnticipatedClosingDate { get; set; }
    public DateOnly? ActualClosingDate { get; set; }

    public List<ParcelConveyanceDocument> ConveyanceDocuments { get; set; } = new();
    public List<ParcelTitleCommitment> TitleCommitments { get; set; } = new();
}

public class ParcelConveyanceDocument : EntityBase
{
    public Guid TitleClosingId { get; set; }
    /// <summary>A value of the Library's conveyance type list.</summary>
    public string? ConveyanceType { get; set; }
    public DateOnly? SignedDate { get; set; }
    public DateOnly? RecordedDate { get; set; }
    public string? RecordingNumber { get; set; }
}

public class ParcelTitleCommitment : EntityBase
{
    public Guid TitleClosingId { get; set; }
    public string VestedOwnerName { get; set; } = default!;
    public DateOnly? RequestedDate { get; set; }
    public DateOnly? ReceivedDate { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? IssuedDate { get; set; }
    public DateOnly? CurativePlanSubmittedDate { get; set; }
    public DateOnly? CurativePlanApprovedDate { get; set; }
}

/// <summary>
/// An appraisal of a parcel (Phase 1.5 Appraisal tab, ADR 0018): who appraises it and when, the amounts, its review and the concurrence it
/// needs. Many per parcel, numbered "APR-&lt;parcel number&gt;-&lt;sequence&gt;". The review fields are the Appraisal Approver's to write.
/// </summary>
public class ParcelAppraisal : EntityBase
{
    public Guid ParcelId { get; set; }
    public int Sequence { get; set; }
    public string Number { get; set; } = default!;
    /// <summary>A value of the Library's appraisal type list.</summary>
    public string? AppraisalType { get; set; }
    /// <summary>The name of a person in the Library's Agents list.</summary>
    public string? Appraiser { get; set; }
    public bool LandPlannerRequired { get; set; }
    public string? AssignedTo { get; set; }
    public DateOnly? RequestedDate { get; set; }
    public DateOnly? ReceivedDate { get; set; }
    public DateOnly? ReportDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? SubmittedToAtpDate { get; set; }
    public DateOnly? EffectiveDate { get; set; }

    // ── review: written by the review roles only ──
    public string? ReviewAppraiser { get; set; }
    public string? ReviewType { get; set; }
    public DateOnly? AppraisalReceivedDate { get; set; }
    public decimal? ApprovedJustCompensation { get; set; }
    public DateOnly? ReviewCompletedDate { get; set; }
    public DateOnly? ReviewSubmittedDate { get; set; }
    public string? ReviewRecommendation { get; set; }
    public decimal? ReviewRecommendedValue { get; set; }

    // ── concurrence, if required ──
    public DateOnly? SubmittedToJatDate { get; set; }
    public DateOnly? ApprovedByJatDate { get; set; }
    public bool FtaConcurrenceNeeded { get; set; }

    public List<ParcelAppraisalAmount> Amounts { get; set; } = new();
}

public class ParcelAppraisalAmount : EntityBase
{
    public Guid AppraisalId { get; set; }
    /// <summary>A value of the Library's appraisal amount type list.</summary>
    public string AmountType { get; set; } = default!;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
}
