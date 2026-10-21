using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>Permission to enter a parcel before it is acquired (surveys, appraisal inspections, testing) (ROW-077).</summary>
public class RightOfEntry : EntityBase
{
    public Guid ParcelId { get; set; }
    public RightOfEntryStatus Status { get; set; }
    public DateOnly? RequestedOn { get; set; }
    public DateOnly? GrantedOn { get; set; }
    /// <summary>When the permission ends. A granted right past this date reads as expired.</summary>
    public DateOnly? ExpiresOn { get; set; }
    /// <summary>Who gave the permission, as written (an owner or their representative).</summary>
    public string? GrantedBy { get; set; }
    public string? Purpose { get; set; }
    /// <summary>Special instructions for the entry.</summary>
    public string? Notes { get; set; }
    // The letter to the owner and its hand-off to the agency's title partner (ADR 0023: the Negotiation tab's one right-of-entry form).
    public DateOnly? LetterMailedDate { get; set; }
    public DateOnly? LetterDeliveredDate { get; set; }
    public DateOnly? SentToAtpDate { get; set; }
}

/// <summary>A request to acquire a property, made by someone inside or outside the agency, awaiting approval (ROW-080).</summary>
public class AcquisitionRequest : EntityBase
{
    public Guid? ProjectId { get; set; }
    public RequesterType RequesterType { get; set; }
    public string RequesterName { get; set; } = default!;
    public string? RequesterEmail { get; set; }
    /// <summary>The property wanted: address, parcel or description.</summary>
    public string Property { get; set; } = default!;
    /// <summary>What the requester wants: interest, timing, purpose.</summary>
    public string? DesiredParameters { get; set; }
    public decimal? EstimatedAmount { get; set; }
    public AcquisitionRequestStatus Status { get; set; } = AcquisitionRequestStatus.Submitted;
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    /// <summary>Optimistic concurrency token (Postgres xmin): two people deciding at once cannot both win.</summary>
    public uint Version { get; set; }
}

/// <summary>An engineer's or surveyor's review of the plans for a parcel (ROW-239).</summary>
public class ParcelPlanReview : EntityBase
{
    public Guid ParcelId { get; set; }
    public ReviewDiscipline Discipline { get; set; }
    public ReviewOutcome Outcome { get; set; }
    public DateOnly ReviewedOn { get; set; }
    public string ReviewerName { get; set; } = default!;
    public string? Notes { get; set; }
}

/// <summary>A staff report or certificate of necessity, written for a parcel and routed for approval (ROW-240).</summary>
public class ParcelStaffReport : EntityBase
{
    public Guid ParcelId { get; set; }
    public ReportKind Kind { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Draft;
    public string Title { get; set; } = default!;
    public string Body { get; set; } = default!;
    public DateTime? SubmittedAt { get; set; }
    public Guid? SubmittedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public string? DecisionNote { get; set; }

    /// <summary>Optimistic concurrency token (Postgres xmin): two people deciding at once cannot both win.</summary>
    public uint Version { get; set; }
}
