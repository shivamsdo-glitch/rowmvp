using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Aurigo.RowManagement.Application.Persistence;

/// <summary>
/// The slice of the EF context that handlers use. Every tenant-owned set is already scoped to the
/// current tenant by a global query filter; only anonymous auth flows call IgnoreQueryFilters().
/// </summary>
public interface IRowDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<AppUser> AppUsers { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePagePermission> RolePagePermissions { get; }
    DbSet<UserToken> UserTokens { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Document> Documents { get; }
    DbSet<SavedFilterProfile> SavedFilterProfiles { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectCostEstimate> ProjectCostEstimates { get; }
    DbSet<ProjectBudget> ProjectBudgets { get; }
    DbSet<RowAuthorization> RowAuthorizations { get; }
    DbSet<ProjectNumberSettings> ProjectNumberSettings { get; }
    DbSet<AgencySettings> AgencySettings { get; }
    DbSet<ProjectNumberCounter> ProjectNumberCounters { get; }
    DbSet<ParcelStatus> ParcelStatuses { get; }
    DbSet<ParcelGroup> ParcelGroups { get; }
    DbSet<ParentTract> ParentTracts { get; }
    DbSet<Parcel> Parcels { get; }
    DbSet<ParcelInterest> ParcelInterests { get; }
    DbSet<ParcelMilestone> ParcelMilestones { get; }
    DbSet<ParcelSiteVisit> ParcelSiteVisits { get; }
    DbSet<ParcelStepEvent> ParcelStepEvents { get; }
    DbSet<CustomFieldDefinition> CustomFieldDefinitions { get; }
    DbSet<WorkflowTemplate> WorkflowTemplates { get; }
    DbSet<WorkflowStep> WorkflowSteps { get; }
    DbSet<DeadlineRule> DeadlineRules { get; }
    DbSet<StepGateSettings> StepGateSettings { get; }
    DbSet<ProgramSettings> ProgramSettings { get; }
    DbSet<ProgramStepTeam> ProgramStepTeams { get; }
    DbSet<Party> Parties { get; }
    DbSet<LibraryField> LibraryFields { get; }
    DbSet<LibraryValue> LibraryValues { get; }
    DbSet<LibraryAgent> LibraryAgents { get; }
    DbSet<ParcelTeamMember> ParcelTeamMembers { get; }
    DbSet<ParcelDiaryEntry> ParcelDiaryEntries { get; }
    DbSet<ParcelNote> ParcelNotes { get; }
    DbSet<ParcelContactLogEntry> ParcelContactLogEntries { get; }
    DbSet<ParcelContactLogLink> ParcelContactLogLinks { get; }
    DbSet<ParcelEasement> ParcelEasements { get; }
    DbSet<PartyAddress> PartyAddresses { get; }
    DbSet<PartyContact> PartyContacts { get; }
    DbSet<ParcelEncumbrance> ParcelEncumbrances { get; }
    DbSet<ParcelEncumbranceLink> ParcelEncumbranceLinks { get; }
    DbSet<ParcelTitleClosing> ParcelTitleClosings { get; }
    DbSet<ParcelConveyanceDocument> ParcelConveyanceDocuments { get; }
    DbSet<ParcelTitleCommitment> ParcelTitleCommitments { get; }
    DbSet<ParcelAppraisal> ParcelAppraisals { get; }
    DbSet<ParcelAppraisalAmount> ParcelAppraisalAmounts { get; }
    DbSet<ParcelNegotiation> ParcelNegotiations { get; }
    DbSet<NegotiationNote> NegotiationNotes { get; }
    DbSet<NegotiationOffer> NegotiationOffers { get; }
    DbSet<NegotiationSettlement> NegotiationSettlements { get; }
    DbSet<ParcelQuestionnaire> ParcelQuestionnaires { get; }
    DbSet<QuestionnaireInterest> QuestionnaireInterests { get; }
    DbSet<QuestionnaireQuestion> QuestionnaireQuestions { get; }
    DbSet<RecordStatusNote> RecordStatusNotes { get; }
    DbSet<ParcelCondemnation> ParcelCondemnations { get; }
    DbSet<CondemnationAttorney> CondemnationAttorneys { get; }
    DbSet<CondemnationCommissioner> CondemnationCommissioners { get; }
    DbSet<CondemnationNotice> CondemnationNotices { get; }
    DbSet<ParcelPropertyRecord> ParcelPropertyRecords { get; }
    DbSet<PropertyFunding> PropertyFundings { get; }
    DbSet<ParcelRelocation> ParcelRelocations { get; }
    DbSet<RelocationAgentRow> RelocationAgents { get; }
    DbSet<RelocationContact> RelocationContacts { get; }
    DbSet<RelocationEligibility> RelocationEligibilities { get; }
    DbSet<RelocationContactLogRow> RelocationContactLogRows { get; }
    DbSet<RelocationPayment> RelocationPayments { get; }
    DbSet<RelocationPaymentApproval> RelocationPaymentApprovals { get; }
    DbSet<RelocationPaymentBand> RelocationPaymentBands { get; }
    DbSet<RightOfEntry> RightsOfEntry { get; }
    DbSet<AcquisitionRequest> AcquisitionRequests { get; }
    DbSet<ParcelPlanReview> ParcelPlanReviews { get; }
    DbSet<ParcelStaffReport> ParcelStaffReports { get; }
    DbSet<PartyLink> PartyLinks { get; }

    /// <summary>Exposed so a handler that recovers from a failed save can discard the unsaved changes.</summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
