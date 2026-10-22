using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>A plain dated note on how a condemnation, property management record or relocation stands. <see cref="OwnerKind"/> says which.</summary>
public class RecordStatusNote : EntityBase
{
    public string OwnerKind { get; set; } = default!;
    public Guid OwnerId { get; set; }
    public DateOnly NoteDate { get; set; }
    public string Text { get; set; } = default!;
}

public static class StatusNoteKinds
{
    public const string Condemnation = "Condemnation";
    public const string PropertyManagement = "PropertyManagement";
    public const string Relocation = "Relocation";
}

// ── Condemnation (Phase 1.5, ADR 0020): one record per parcel, "CON-<parcel number>" ──

public class ParcelCondemnation : EntityBase
{
    public Guid ParcelId { get; set; }
    public string Number { get; set; } = default!;

    // Preliminary condemnation
    public DateOnly? PrelimPackageSubmittedToAtp { get; set; }
    public DateOnly? PrelimPackageApprovedByAtp { get; set; }
    public DateOnly? PrelimConcurrenceMemoSentToFta { get; set; }
    public DateOnly? JatApprovalOfPrelimPackage { get; set; }
    public DateOnly? JatApprovedPackageToCityLegal { get; set; }
    public DateOnly? CityLegalReviewCompleted { get; set; }
    public DateOnly? PrelimPackageSubmittedForCouncil { get; set; }
    public DateOnly? CityCouncilApproval { get; set; }

    // Condemnation support (people are names from the Library's Agents list)
    public string? EdCoordinator { get; set; }
    public string? OutsideCounsel { get; set; }
    public string? AppraisalWitness { get; set; }
    public string? EngineeringWitness { get; set; }
    public string? ExpertWitness1 { get; set; }
    public string? ExpertWitness2 { get; set; }
    public DateOnly? PetitionPackageReceived { get; set; }
    public DateOnly? PetitionPackageReviewedByAtp { get; set; }
    public string? ProbateCauseNo { get; set; }
    public DateOnly? PetitionFiledDate { get; set; }
    public DateOnly? LisPendensRecordedDate { get; set; }
    public DateOnly? LisPendensLettersMailedDate { get; set; }
    public DateOnly? CommissionerStrikePeriodExpires { get; set; }
    public DateOnly? CommissionerNotificationLettersMailed { get; set; }
    public DateOnly? OrderSettingHearingDate { get; set; }
    public DateOnly? OathsOfCommissionersDate { get; set; }
    public DateOnly? PrehearingScheduled { get; set; }
    public DateOnly? CommissionerFeesDeposited { get; set; }
    public DateOnly? ObjectionDeadline { get; set; }
    public bool ObjectionsFiled { get; set; }

    public List<CondemnationAttorney> Attorneys { get; set; } = new();
    public List<CondemnationCommissioner> Commissioners { get; set; } = new();
    public List<CondemnationNotice> Notices { get; set; } = new();
}

public class CondemnationAttorney : EntityBase
{
    public Guid CondemnationId { get; set; }
    public string PartyRepresented { get; set; } = default!;
    public string? AttorneyName { get; set; }
    public string? Firm { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
}

public class CondemnationCommissioner : EntityBase
{
    public Guid CondemnationId { get; set; }
    public DateOnly? AppointmentDate { get; set; }
    public string? AppointmentType { get; set; }
    public string Name { get; set; } = default!;
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool Struck { get; set; }
}

public class CondemnationNotice : EntityBase
{
    public Guid CondemnationId { get; set; }
    public string PartyToServe { get; set; } = default!;
    public DateOnly? ServiceSubmitted { get; set; }
    public DateOnly? ServiceCompleted { get; set; }
    public DateOnly? NohFiled { get; set; }
    public string? Comments { get; set; }
}

// ── Property Management & Disposition: one record per parcel, "PMD-<parcel number>" ──

public class ParcelPropertyRecord : EntityBase
{
    public Guid ParcelId { get; set; }
    public string Number { get; set; } = default!;
    public DateOnly? TransferMemoSubmittedToAtp { get; set; }
    public DateOnly? TransferMemoApprovedByAtp { get; set; }
    public DateOnly? TransferMemoToPdb { get; set; }
    public DateOnly? PropertySecured { get; set; }
    /// <summary>A value of the Library's demo status list.</summary>
    public string? DemoStatus { get; set; }
    public DateOnly? DemoCompleted { get; set; }
    public string? DispositionType { get; set; }
    public string? DispositionMethod { get; set; }
    public string? DispositionStatus { get; set; }
    public string? DispositionJustification { get; set; }
    public string? BuyerGrantee { get; set; }
    public bool FtaConcurrenceRequired { get; set; }
    public decimal? SalePrice { get; set; }
    public DateOnly? LegalReviewCompleteDate { get; set; }
    public DateOnly? BoardApprovalDate { get; set; }
    public DateOnly? ClosingTransferDate { get; set; }
    public DateOnly? RecordingDate { get; set; }
    public string? DocumentRecordingNo { get; set; }

    public List<PropertyFunding> Funding { get; set; } = new();
}

public class PropertyFunding : EntityBase
{
    public Guid PropertyRecordId { get; set; }
    /// <summary>A value of the Library's fund type list.</summary>
    public string FundType { get; set; } = default!;
    public string? FundSource { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Percentage { get; set; }
}

// ── Relocation: many per parcel; the relocation number is typed by the user and unique on the parcel ──

public class ParcelRelocation : EntityBase
{
    public Guid ParcelId { get; set; }
    public string RelocationNumber { get; set; } = default!;
    public string? AssignedTo { get; set; }
    public string DisplaceeName { get; set; } = default!;
    public bool LimitedEnglishProficiency { get; set; }
    public string? DisplaceeStatus { get; set; }
    public string? DisplaceeInterest { get; set; }
    public string? DisplaceeMailingAddress { get; set; }
    public string RelocationType { get; set; } = default!;
    public string? PropertySiteAddress { get; set; }

    public DateOnly? GinMailedDate { get; set; }
    public DateOnly? GinDeliveredDate { get; set; }
    public DateOnly? InitialInterview { get; set; }
    public DateOnly? CertificateOfEligibility { get; set; }
    public DateOnly? NoticeOfEligibility { get; set; }
    public DateOnly? DeliveryOfNoticeOfEligibility { get; set; }
    public DateOnly? Notice90Day { get; set; }
    public DateOnly? DeliveryOf90DayNotice { get; set; }
    public DateOnly? VacateEligibleDate { get; set; }
    public DateOnly? Notice30DayVacate { get; set; }
    public DateOnly? Notice30DayVacateDelivered { get; set; }
    public bool DemolitionRequired { get; set; }
    public DateOnly? TargetVacateDate { get; set; }
    public DateOnly? ActualVacateDate { get; set; }
    public bool IneligibleForRelocation { get; set; }
    public DateOnly? MovePlan { get; set; }
    public DateOnly? CertificationOfInventory { get; set; }
    public DateOnly? BenefitsExpirationLetterDate { get; set; }
    public DateOnly? BenefitExpiration { get; set; }
    public DateOnly? ReleaseOfProperty { get; set; }
    public DateOnly? CertificateOfCompletion { get; set; }
    public DateOnly? FileCloseOutSubmitted { get; set; }
    public DateOnly? RelocationComplete { get; set; }
    public DateOnly? RelocationComplete1 { get; set; }

    // ── Case workflow (ADR 0026) ──
    /// <summary>The ownership interest (a <see cref="PartyLink"/>) this relocation is for. Null on records made before ADR 0026.</summary>
    public Guid? PartyLinkId { get; set; }
    public DateOnly? PlanningStudyDate { get; set; }
    public string? AdvisoryNeeds { get; set; }
    public DateOnly? ReplacementHousingStudyDate { get; set; }
    public DateOnly? ComparableNoticeDate { get; set; }
    public DateOnly? InspectionDate { get; set; }
    public bool? DecentSafeSanitary { get; set; }
    public bool HousingOfLastResort { get; set; }
    /// <summary>One of <c>RelocationExceptions</c>, or null.</summary>
    public string? EligibilityException { get; set; }
    public DateOnly? ClaimDeniedDate { get; set; }
    public DateOnly? AppealFiledDate { get; set; }
    public DateOnly? AppealDecisionDate { get; set; }
    public string? AppealOutcome { get; set; }

    public List<RelocationPayment> Payments { get; set; } = new();
    public List<RelocationAgentRow> Agents { get; set; } = new();
    public List<RelocationContact> Contacts { get; set; } = new();
    public List<RelocationEligibility> Eligibilities { get; set; } = new();
    public List<RelocationContactLogRow> ContactLog { get; set; } = new();
}

public static class RelocationExceptions
{
    public const string EvictionForCause = "Eviction for cause";
    public const string NotLawfullyPresent = "Alien not lawfully present";
    public const string Duplication = "Duplication of payment";
    public static readonly IReadOnlyList<string> All = new[] { EvictionForCause, NotLawfullyPresent, Duplication };
}

public enum RelocationPaymentStatus { Requested = 1, Approved = 2, Rejected = 3, Paid = 4 }

/// <summary>A payment request on a relocation (ADR 0026). The amount is typed; the approval steps are fixed from the agency's bands when it is requested.</summary>
public class RelocationPayment : EntityBase
{
    public Guid RelocationId { get; set; }
    public string PaymentType { get; set; } = default!;
    public decimal Amount { get; set; }
    public bool IsAdvance { get; set; }
    /// <summary>Taxable for 1099 reporting (ROW-097).</summary>
    public bool Taxable { get; set; }
    public string? Notes { get; set; }
    public RelocationPaymentStatus Status { get; set; } = RelocationPaymentStatus.Requested;
    /// <summary>The ordered roles that must sign, comma separated, as evaluated when the request was made.</summary>
    public string RequiredRoles { get; set; } = default!;
    public string? DecisionNote { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? PaymentReference { get; set; }
    public List<RelocationPaymentApproval> Approvals { get; set; } = new();
}

public class RelocationPaymentApproval : EntityBase
{
    public Guid PaymentId { get; set; }
    public string Role { get; set; } = default!;
    public Guid ApprovedBy { get; set; }
    public DateTime ApprovedAt { get; set; }
}

/// <summary>One band of the relocation payment approval chain. A tenant with no rows uses the defaults in <c>DefaultRelocationBands</c>.</summary>
public class RelocationPaymentBand : EntityBase
{
    public decimal MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public string Roles { get; set; } = default!;
}

public class RelocationAgentRow : EntityBase
{
    public Guid RelocationId { get; set; }
    public string AgentName { get; set; } = default!;
}

public class RelocationContact : EntityBase
{
    public Guid RelocationId { get; set; }
    public string Name { get; set; } = default!;
    public string? ContactType { get; set; }
    public string? Contact { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool IsPrimary { get; set; }
}

public class RelocationEligibility : EntityBase
{
    public Guid RelocationId { get; set; }
    /// <summary>A value of the Library's relocation eligibility type list.</summary>
    public string EligibilityType { get; set; } = default!;
    public decimal? ClaimLimit { get; set; }
    public DateOnly? SubmittedForApproval { get; set; }
    public DateOnly? Approved { get; set; }
}

public class RelocationContactLogRow : EntityBase
{
    public Guid RelocationId { get; set; }
    public DateTime ContactedAt { get; set; }
    public string? Mode { get; set; }
    public string? ContactedBy { get; set; }
    public string? ContactPersons { get; set; }
    public string? Notes { get; set; }
}
