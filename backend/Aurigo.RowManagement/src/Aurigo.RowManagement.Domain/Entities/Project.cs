using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>A ROW project: the transportation project that needs parcels of land (ROW-001, 003, 004, 076).</summary>
public class Project : EntityBase
{
    /// <summary>Unique within the tenant. Generated from the tenant's format, typed by hand, or imported.</summary>
    public string Number { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }

    public string? Region { get; set; }
    public string? District { get; set; }

    public OversightType OversightType { get; set; }
    public string? FundingSource { get; set; }
    public decimal? FundingAmount { get; set; }
    public decimal? DesignEstimate { get; set; }

    public DateOnly? LettingDate { get; set; }
    public DateOnly? PlanTurnInDate { get; set; }
    public DateOnly? ConstructionStartDate { get; set; }
    public DateOnly? ConstructionEndDate { get; set; }

    public NepaDecisionType? NepaDecisionType { get; set; }
    public DateOnly? NepaDecisionDate { get; set; }

    public AcquisitionExceptionType? ExceptionType { get; set; }
    public DateOnly? ExceptionApprovedOn { get; set; }
    public string? ExceptionReason { get; set; }

    public List<ProjectCostEstimate> Estimates { get; set; } = new();
    public List<ProjectBudget> Budgets { get; set; } = new();
    public List<RowAuthorization> Authorizations { get; set; } = new();
}

/// <summary>
/// One immutable version of the project's ROW cost estimate (ROW-012). A change is a new version;
/// earlier versions are kept and never edited.
/// </summary>
public class ProjectCostEstimate : EntityBase
{
    public Guid ProjectId { get; set; }
    public int Version { get; set; }
    public decimal LandCost { get; set; }
    public decimal ImprovementsCost { get; set; }
    public decimal RelocationCost { get; set; }
    /// <summary>Appraisal, negotiation, closing, title insurance and other indirect costs.</summary>
    public decimal IndirectCost { get; set; }
    public decimal Total { get; set; }
    public string? Notes { get; set; }

    public void RecalculateTotal() => Total = LandCost + ImprovementsCost + RelocationCost + IndirectCost;
}

/// <summary>The ROW budget for one fiscal year (ROW-013). Adjustments overwrite the amount; the audit log keeps the history.</summary>
public class ProjectBudget : EntityBase
{
    public Guid ProjectId { get; set; }
    public int FiscalYear { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}

/// <summary>A preliminary, acquisition or supplemental ROW authorization (ROW-238).</summary>
public class RowAuthorization : EntityBase
{
    public Guid ProjectId { get; set; }
    public AuthorizationType Type { get; set; }
    public decimal AuthorizedAmount { get; set; }
    /// <summary>Federal share of the authorized amount as a percentage, 0 to 100; null when not federal-aid.</summary>
    public decimal? FederalSharePercent { get; set; }
    public DateOnly AuthorizedOn { get; set; }
    public string? ReferenceNumber { get; set; }
    /// <summary>Programming (STIP / TIP) confirmed.</summary>
    public bool ProgrammingConfirmed { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// A tenant's project-number format and fiscal-year start (ROW-001). One row per tenant; when absent the
/// defaults apply. Pattern tokens: {FY}, {YY}, {SEQ:n}.
/// </summary>
public class ProjectNumberSettings : EntityBase
{
    public string Pattern { get; set; } = "{FY}-{SEQ:4}";
    public int FiscalYearStartMonth { get; set; } = 1;
}

/// <summary>The last sequence value issued in one numbering scope (a fiscal year, or "all" when the pattern has no year).</summary>
public class ProjectNumberCounter : EntityBase
{
    public string ScopeKey { get; set; } = default!;
    public int LastValue { get; set; }

    /// <summary>Optimistic concurrency token (Postgres xmin) so two simultaneous creates cannot issue the same number.</summary>
    public uint Version { get; set; }
}
