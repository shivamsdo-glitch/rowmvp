namespace Aurigo.RowManagement.Domain.Enums;

/// <summary>Which workflow template a parcel follows (ROW-071). Phase 4 builds the non-standard paths themselves.</summary>
public enum AcquisitionType
{
    Standard = 1,
    Donation = 2,
    DeveloperConveyance = 3,
    Early = 4,
}

/// <summary>
/// Whether a parcel is live. Splitting or combining supersedes the source parcels: they stay on record (documents, history)
/// but drop out of lists, roll-ups and reminders (ROW-007).
/// </summary>
public enum ParcelState
{
    Active = 1,
    Split = 2,
    Combined = 3,
}

/// <summary>The interest taken in a parcel and its area (ROW-234).</summary>
public enum InterestType
{
    FeeTaking = 1,
    Remainder = 2,
    PermanentEasement = 3,
    UtilityEasement = 4,
    TemporaryEasement = 5,
}

public enum CustomFieldType
{
    Text = 1,
    Number = 2,
    Date = 3,
    Boolean = 4,
    List = 5,
}

/// <summary>How the written Notice to Owner was delivered (ROW-237).</summary>
public enum NoticeMethod
{
    CertifiedMail = 1,
    FirstClassMail = 2,
    HandDelivery = 3,
    Email = 4,
    Posting = 5,
}
