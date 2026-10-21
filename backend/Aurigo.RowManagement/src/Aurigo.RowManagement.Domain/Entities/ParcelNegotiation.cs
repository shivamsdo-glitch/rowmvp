using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// A parcel's negotiation record (Phase 1.5 Negotiation tab, ADR 0019): who negotiates, offers, administrative settlement
/// counteroffers, the JAT counteroffer approval and the possession and use agreement. One per parcel, numbered "NEG-&lt;parcel number&gt;".
/// </summary>
public class ParcelNegotiation : EntityBase
{
    public Guid ParcelId { get; set; }
    public string Number { get; set; } = default!;
    /// <summary>The name of a person on the parcel's team.</summary>
    public string? AssignedTo { get; set; }
    public decimal? TotalAgreementAmount { get; set; }
    public string? Notes { get; set; }
    public DateOnly? CounterofferSubmittedToJatDate { get; set; }
    public DateOnly? CounterofferResponseFromJatDate { get; set; }
    /// <summary>A value of the Library's JAT response list.</summary>
    public string? JatResponse { get; set; }
    public bool FtaApprovalRequiredForCounteroffer { get; set; }
    public DateOnly? PuaRequestedDate { get; set; }
    public DateOnly? PuaApprovedDate { get; set; }
    public decimal? PuaApprovedAmount { get; set; }
    public DateOnly? PuaExecutedDate { get; set; }
    public DateOnly? PuaClosedDate { get; set; }

    public List<NegotiationNote> StatusNotes { get; set; } = new();
    public List<NegotiationOffer> Offers { get; set; } = new();
    public List<NegotiationSettlement> Settlements { get; set; } = new();
}

/// <summary>A plain dated note on how the negotiation stands.</summary>
public class NegotiationNote : EntityBase
{
    public Guid NegotiationId { get; set; }
    public DateOnly NoteDate { get; set; }
    public string Text { get; set; } = default!;
}

public class NegotiationOffer : EntityBase
{
    public Guid NegotiationId { get; set; }
    /// <summary>A value of the Library's offer type list.</summary>
    public string? OfferType { get; set; }
    public DateOnly? MailedDate { get; set; }
    public DateOnly? DeliveredDate { get; set; }
    public decimal? Amount { get; set; }
    public string? Notes { get; set; }
}

public class NegotiationSettlement : EntityBase
{
    public Guid NegotiationId { get; set; }
    /// <summary>A value of the Library's settlement type list.</summary>
    public string? SettlementType { get; set; }
    public decimal? CounterofferAmount { get; set; }
    public DateOnly? DateReceived { get; set; }
    public DateOnly? DateSubmitted { get; set; }
    /// <summary>A value of the Library's counteroffer response list.</summary>
    public string? CounterofferResponse { get; set; }
    public DateOnly? ResponseDateFromAtp { get; set; }
    public DateOnly? ResponseDateToOwner { get; set; }
    public string? SettlementNotes { get; set; }
}
