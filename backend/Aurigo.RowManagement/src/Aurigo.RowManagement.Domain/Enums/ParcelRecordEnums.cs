namespace Aurigo.RowManagement.Domain.Enums;

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
