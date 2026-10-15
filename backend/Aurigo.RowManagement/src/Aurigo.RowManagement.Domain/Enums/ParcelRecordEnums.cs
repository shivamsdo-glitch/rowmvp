namespace Aurigo.RowManagement.Domain.Enums;

/// <summary>The roles people hold on a parcel's team (ROW-019). A person who fits none is "Other" with a note.</summary>
public enum TeamRole
{
    ProjectLead = 1,
    AppraisalCoordinator = 2,
    Appraiser = 3,
    Reviewer = 4,
    Negotiator = 5,
    Counsel = 6,
    RelocationAgent = 7,
    Fhwa = 8,
    Other = 9,
}

/// <summary>How a contact in the negotiation diary was made (ROW-020).</summary>
public enum DiaryMethod
{
    Call = 1,
    Meeting = 2,
    Mailing = 3,
    Email = 4,
    ContactAttempt = 5,
    Other = 6,
}

public enum DiaryDirection
{
    Outgoing = 1,
    Incoming = 2,
}
