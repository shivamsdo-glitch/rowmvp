namespace Aurigo.RowManagement.Domain.Enums;

/// <summary>Where a right of entry stands (ROW-077). "Expired" is not stored: it is a granted right whose end date has passed.</summary>
public enum RightOfEntryStatus
{
    Requested = 1,
    Granted = 2,
    Denied = 3,
    Revoked = 4,
}

/// <summary>Where an acquisition request stands (ROW-080).</summary>
public enum AcquisitionRequestStatus
{
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Withdrawn = 4,
}

public enum RequesterType
{
    Internal = 1,
    External = 2,
}

/// <summary>The disciplines whose plan review a parcel needs when the agency turns the gate on (ROW-239).</summary>
public enum ReviewDiscipline
{
    Engineer = 1,
    Surveyor = 2,
}

public enum ReviewOutcome
{
    Approved = 1,
    ChangesRequired = 2,
}

/// <summary>The two documents prepared and routed for approval (ROW-240).</summary>
public enum ReportKind
{
    StaffReport = 1,
    CertificateOfNecessity = 2,
}

public enum ReportStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
}
