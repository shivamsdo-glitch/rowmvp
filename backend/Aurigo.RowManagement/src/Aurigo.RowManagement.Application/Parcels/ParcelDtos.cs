using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Application.Parcels;

public sealed record ParcelStatusDto(Guid Id, string Name, string Color, int SortOrder, bool IsActive, bool IsInitial);

public sealed record ParcelGroupDto(Guid Id, Guid ProjectId, string Name, string? Description, int ParcelCount);

public sealed record ParentTractDto(Guid Id, Guid ProjectId, string Number, string? Description, IReadOnlyList<string> TaxParcelNumbers, int ParcelCount);

public sealed record ParcelSummaryDto(
    Guid Id, Guid ProjectId, string ProjectNumber, string Number, string? Address, decimal AreaSqFt, string? PropertyType,
    Guid StatusId, string StatusName, string StatusColor, Guid? GroupId, string? GroupName, Guid? ParentTractId, string? ParentTractNumber,
    AcquisitionType AcquisitionType, string CurrentStepKey, bool IsRemnant, ParcelState State);

public sealed record ParcelInterestDto(Guid Id, InterestType Type, decimal AreaSqFt, string? Notes);

public sealed record ParcelMilestoneDto(Guid Id, string Name, DateOnly? DueOn, DateOnly? CompletedOn, string? Notes, bool IsStandard, bool IsOverdue);

public sealed record ParcelSiteVisitDto(Guid Id, DateOnly VisitedOn, string? Notes, bool OwnerContactConfirmed);

public sealed record ParcelStepEventDto(Guid Id, string? FromStepKey, string ToStepKey, bool IsOverride, string? Reason, DateTime At, Guid By);

public sealed record DeadlineDto(
    Guid RuleId, string Name, string TriggerMilestone, string TargetMilestone, int Days,
    DateOnly? TriggerDate, DateOnly? DueOn, string Status, int? DaysRemaining);

public sealed record WorkflowStepDto(string Key, string Label, int Order, bool RequiresAcquisitionGate);

public sealed record WorkflowDto(AcquisitionType AcquisitionType, IReadOnlyList<WorkflowStepDto> Steps);

/// <summary>Where a parcel is in its workflow and what the caller may do next.</summary>
public sealed record WorkflowStateDto(
    IReadOnlyList<WorkflowStepDto> Steps, string CurrentKey, string? NextKey, bool AcquisitionGateOpen, bool CanOverride);

public sealed record ParcelDto(
    Guid Id, Guid ProjectId, string ProjectNumber, string ProjectName, string Number, string? LegalDescription, string? Address,
    decimal AreaSqFt, string? PropertyType, Guid StatusId, string StatusName, string StatusColor,
    Guid? GroupId, string? GroupName, Guid? ParentTractId, string? ParentTractNumber, IReadOnlyList<string> TaxParcelNumbers,
    AcquisitionType AcquisitionType, ParcelState State, DateTime? SupersededOn, IReadOnlyList<Guid> SourceParcelIds, bool IsRemnant,
    IReadOnlyDictionary<string, string> CustomValues, DateOnly? NoticeIssuedOn, NoticeMethod? NoticeMethod,
    IReadOnlyList<ParcelInterestDto> Interests, IReadOnlyList<ParcelMilestoneDto> Milestones, IReadOnlyList<ParcelSiteVisitDto> SiteVisits,
    IReadOnlyList<DeadlineDto> Deadlines, WorkflowStateDto Workflow, IReadOnlyList<ParcelStepEventDto> StepHistory, DateTime UpdatedAt);

public sealed record RollupBucketDto(Guid? Id, string Name, string? Color, int Count, decimal AreaSqFt, decimal Acres);

public sealed record ParcelRollupDto(
    int TotalCount, decimal TotalAreaSqFt, decimal TotalAcres, IReadOnlyList<RollupBucketDto> ByStatus, IReadOnlyList<RollupBucketDto> ByGroup);

public sealed record ParcelSplitResultDto(ParcelDto Source, IReadOnlyList<ParcelSummaryDto> Parts, ParcelSummaryDto? Remnant);

public sealed record BatchUpdateResultDto(int Updated);

public sealed record ReminderDto(
    Guid ParcelId, string ParcelNumber, Guid ProjectId, string ProjectNumber, string RuleName, string TargetMilestone,
    DateOnly DueOn, string Status, int DaysRemaining);

public sealed record CustomFieldDto(
    Guid Id, string EntityType, string Key, string Label, CustomFieldType FieldType, IReadOnlyList<string> Options,
    bool IsRequired, bool IsActive, int SortOrder);

public sealed record DeadlineRuleDto(Guid Id, string Name, string TriggerMilestone, string TargetMilestone, int Days, bool IsActive, bool IsPlaceholder);

public sealed record StepGateSettingsDto(IReadOnlyList<string> AlwaysRoles, IReadOnlyList<string> ExtraOverrideRoles, IReadOnlyList<string> AssignableRoles);
