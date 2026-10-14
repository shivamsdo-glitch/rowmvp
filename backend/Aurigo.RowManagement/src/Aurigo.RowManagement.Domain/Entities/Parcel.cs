using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// An entry in the agency's manually-set parcel status list (ROW-005, user decision in README). Statuses are data:
/// the board colours its columns from <see cref="Color"/>.
/// </summary>
public class ParcelStatus : EntityBase
{
    public string Name { get; set; } = default!;
    /// <summary>A "#rrggbb" colour.</summary>
    public string Color { get; set; } = "#64748b";
    public int SortOrder { get; set; }
    /// <summary>Inactive statuses stay on parcels that have them but cannot be chosen again.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>The status new parcels start in. Exactly one active status is the initial one.</summary>
    public bool IsInitial { get; set; }
}

/// <summary>The second WBS level under a project: a corridor, segment or other grouping of parcels (ROW-002, 006).</summary>
public class ParcelGroup : EntityBase
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
}

/// <summary>An owner's whole tract, linked to its tax parcel numbers; parcels are the takes from it (ROW-233).</summary>
public class ParentTract : EntityBase
{
    public Guid ProjectId { get; set; }
    public string Number { get; set; } = default!;
    public string? Description { get; set; }
    /// <summary>APN / GPIN values from the assessor's records.</summary>
    public List<string> TaxParcelNumbers { get; set; } = new();
}

/// <summary>One take of land for a project (ROW-005): the unit that is appraised, negotiated and closed.</summary>
public class Parcel : EntityBase
{
    public Guid ProjectId { get; set; }
    public Guid? ParentTractId { get; set; }
    public Guid? GroupId { get; set; }

    /// <summary>Unique within the project.</summary>
    public string Number { get; set; } = default!;
    public string? LegalDescription { get; set; }
    public string? Address { get; set; }
    public decimal AreaSqFt { get; set; }
    public string? PropertyType { get; set; }

    public Guid StatusId { get; set; }

    public AcquisitionType AcquisitionType { get; set; } = AcquisitionType.Standard;
    /// <summary>The workflow step the parcel is on (ROW-072); a key from its workflow template.</summary>
    public string CurrentStepKey { get; set; } = default!;

    public ParcelState State { get; set; } = ParcelState.Active;
    public DateTime? SupersededOn { get; set; }
    /// <summary>The parcels this one was split from (one) or combined from (several); empty for an original parcel.</summary>
    public List<Guid> SourceParcelIds { get; set; } = new();
    /// <summary>The leftover piece created when a parcel is split (ROW-007).</summary>
    public bool IsRemnant { get; set; }

    /// <summary>Values of the tenant's custom fields (ROW-011) as a JSON object keyed by field key.</summary>
    public string CustomValuesJson { get; set; } = "{}";

    public DateOnly? NoticeIssuedOn { get; set; }
    public NoticeMethod? NoticeMethod { get; set; }

    /// <summary>
    /// Optimistic concurrency token (Postgres xmin). Two people changing the same parcel at once, or combining and splitting it
    /// at once, cannot both succeed: the second save is refused instead of silently overwriting or double-using the parcel.
    /// </summary>
    public uint Version { get; set; }

    public List<ParcelInterest> Interests { get; set; } = new();
    public List<ParcelMilestone> Milestones { get; set; } = new();
    public List<ParcelSiteVisit> SiteVisits { get; set; } = new();
    public List<ParcelStepEvent> StepEvents { get; set; } = new();
}

public class ParcelInterest : EntityBase
{
    public Guid ParcelId { get; set; }
    public InterestType Type { get; set; }
    public decimal AreaSqFt { get; set; }
    public string? Notes { get; set; }
}

/// <summary>A dated milestone on a parcel; completed when <see cref="CompletedOn"/> is set (ROW-010).</summary>
public class ParcelMilestone : EntityBase
{
    public Guid ParcelId { get; set; }
    public string Name { get; set; } = default!;
    public DateOnly? DueOn { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public string? Notes { get; set; }
}

public class ParcelSiteVisit : EntityBase
{
    public Guid ParcelId { get; set; }
    public DateOnly VisitedOn { get; set; }
    public string? Notes { get; set; }
    public bool OwnerContactConfirmed { get; set; }
}

/// <summary>
/// One move along the workflow, kept as the audit trail of step gating (ROW-072). An override is a move that skipped,
/// repeated or reversed the order; it always carries the reason and who did it (CreatedBy).
/// </summary>
public class ParcelStepEvent : EntityBase
{
    public Guid ParcelId { get; set; }
    public string? FromStepKey { get; set; }
    public string ToStepKey { get; set; } = default!;
    public bool IsOverride { get; set; }
    public string? Reason { get; set; }
}

/// <summary>A tenant-defined attribute on parcels (ROW-011).</summary>
public class CustomFieldDefinition : EntityBase
{
    /// <summary>The kind of record the field belongs to. "Parcel" for now.</summary>
    public string EntityType { get; set; } = "Parcel";
    /// <summary>Stable machine key, unique per entity type.</summary>
    public string Key { get; set; } = default!;
    public string Label { get; set; } = default!;
    public CustomFieldType FieldType { get; set; }
    /// <summary>The allowed values of a List field.</summary>
    public List<string> Options { get; set; } = new();
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>The ordered steps parcels of one acquisition type move through (ROW-071, 072).</summary>
public class WorkflowTemplate : EntityBase
{
    public AcquisitionType AcquisitionType { get; set; }
    public List<WorkflowStep> Steps { get; set; } = new();
}

public class WorkflowStep : EntityBase
{
    public Guid TemplateId { get; set; }
    /// <summary>Stable key, unique within the template.</summary>
    public string Key { get; set; } = default!;
    public string Label { get; set; } = default!;
    public int Order { get; set; }
    /// <summary>Offers and negotiations start at this step, so the project's acquisition gate (NEPA, authorization) must be open.</summary>
    public bool RequiresAcquisitionGate { get; set; }
}

/// <summary>
/// A statutory or agency timeline (ROW-073): the target milestone is due this many days after the trigger milestone is
/// completed. The periods are agency data, never code; seeded rules are placeholders until the agency confirms them.
/// </summary>
public class DeadlineRule : EntityBase
{
    public string Name { get; set; } = default!;
    public string TriggerMilestone { get; set; } = default!;
    public string TargetMilestone { get; set; } = default!;
    public int Days { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>A seeded value nobody has confirmed. Cleared when an administrator edits the rule.</summary>
    public bool IsPlaceholder { get; set; }
}

/// <summary>
/// Who may override step order (ROW-072). Admin and ProjectManager always can; an administrator may add more roles here.
/// One row per tenant.
/// </summary>
public class StepGateSettings : EntityBase
{
    public List<string> ExtraOverrideRoles { get; set; } = new();
}
