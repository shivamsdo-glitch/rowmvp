namespace Aurigo.RowManagement.Domain.Enums;

/// <summary>Who oversees the project's funding. Drives the acquisition gate (ROW-004, ROW-076).</summary>
public enum OversightType
{
    Federal = 1,
    State = 2,
    Local = 3,
}

/// <summary>The NEPA class of action decided for the project (ROW-076).</summary>
public enum NepaDecisionType
{
    CategoricalExclusion = 1,
    EnvironmentalAssessment = 2,
    EnvironmentalImpactStatement = 3,
}

/// <summary>Preliminary covers appraisal and title only; acquisition and supplemental cover negotiations (ROW-238).</summary>
public enum AuthorizationType
{
    Preliminary = 1,
    Acquisition = 2,
    Supplemental = 3,
}

/// <summary>Approved routes to acquire before NEPA clearance and authorization are on file (Diagram 1, GT_c).</summary>
public enum AcquisitionExceptionType
{
    Early = 1,
    Hardship = 2,
    Protective = 3,
}
