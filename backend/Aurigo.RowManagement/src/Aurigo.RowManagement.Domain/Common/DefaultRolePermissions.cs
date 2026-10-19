namespace Aurigo.RowManagement.Domain.Common;

/// <summary>
/// The out-of-the-box page access for each of the six ROW roles, derived from the swimlanes in
/// vault/diagrams/Diagram00_Final.md and the per-diagram role lanes. A page that is not listed for a
/// role means no access. These are only starting points: an administrator can edit them per tenant,
/// and re-seeding never overwrites an edited row (it only adds rows that are missing).
/// </summary>
public static class DefaultRolePermissions
{
    private const string B = PermissionTemplates.Basic;
    private const string S = PermissionTemplates.Standard;
    private const string P = PermissionTemplates.Pro;

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Matrix =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            // Creates projects and parcels, owns budget/team/milestones, and approves at project level.
            [RoleNames.ProjectManager] = new Dictionary<string, string>
            {
                [PageKeys.Dashboard] = B, [PageKeys.Projects] = P, [PageKeys.Parcels] = P, [PageKeys.Parties] = P,
                [PageKeys.Title] = B, [PageKeys.Survey] = B, [PageKeys.Appraisals] = B, [PageKeys.Approvals] = S,
                [PageKeys.Negotiations] = B, [PageKeys.Agreements] = B, [PageKeys.Closing] = B, [PageKeys.Payments] = B,
            },
            // Title research, survey requests and the appraisal package (Diagram 2).
            [RoleNames.TitleResearcher] = new Dictionary<string, string>
            {
                [PageKeys.Dashboard] = B, [PageKeys.Projects] = B, [PageKeys.Parcels] = B, [PageKeys.Parties] = B,
                [PageKeys.Title] = P, [PageKeys.Survey] = P, [PageKeys.Appraisals] = B,
            },
            // Appraisal and review, and establishing just compensation (Diagram 3).
            [RoleNames.Appraiser] = new Dictionary<string, string>
            {
                [PageKeys.Dashboard] = B, [PageKeys.Projects] = B, [PageKeys.Parcels] = B, [PageKeys.Parties] = B,
                [PageKeys.Title] = B, [PageKeys.Survey] = B, [PageKeys.Appraisals] = P, [PageKeys.Approvals] = S,
            },
            // Offers, negotiation, agreements and closing (Diagram 4); captures owners and interests (Diagram 1).
            [RoleNames.Negotiator] = new Dictionary<string, string>
            {
                [PageKeys.Dashboard] = B, [PageKeys.Projects] = B, [PageKeys.Parcels] = B, [PageKeys.Parties] = P,
                [PageKeys.Title] = B, [PageKeys.Appraisals] = B, [PageKeys.Approvals] = B,
                [PageKeys.Negotiations] = P, [PageKeys.Agreements] = P, [PageKeys.Closing] = S, [PageKeys.Payments] = S,
            },
            // Fund reservation, payments and expenditure (Diagram 7).
            [RoleNames.Finance] = new Dictionary<string, string>
            {
                [PageKeys.Dashboard] = B, [PageKeys.Projects] = B, [PageKeys.Parcels] = B, [PageKeys.Parties] = B,
                [PageKeys.Approvals] = S, [PageKeys.Negotiations] = B, [PageKeys.Agreements] = B,
                [PageKeys.Closing] = B, [PageKeys.Payments] = P,
            },
            // Designer: adds and edits parcel details and reviews plans and ROW maps (Phase 1.5 flow).
            [RoleNames.Designer] = new Dictionary<string, string>
            {
                [PageKeys.Dashboard] = B, [PageKeys.Projects] = B, [PageKeys.Parcels] = P, [PageKeys.Parties] = P,
            },
            // Design approver: approves parcels for acquisition.
            [RoleNames.DesignApprover] = new Dictionary<string, string>
            {
                [PageKeys.Dashboard] = B, [PageKeys.Projects] = B, [PageKeys.Parcels] = S, [PageKeys.Parties] = B,
                [PageKeys.Approvals] = S,
            },
            // Surveyor: right-of-way limits, field data and survey metadata.
            [RoleNames.Surveyor] = new Dictionary<string, string>
            {
                [PageKeys.Dashboard] = B, [PageKeys.Projects] = B, [PageKeys.Parcels] = B, [PageKeys.Parties] = B,
                [PageKeys.Survey] = P,
            },
            // Appraisal approver: reviews appraisals and approves just compensation.
            [RoleNames.AppraisalApprover] = new Dictionary<string, string>
            {
                [PageKeys.Dashboard] = B, [PageKeys.Projects] = B, [PageKeys.Parcels] = B, [PageKeys.Parties] = B,
                [PageKeys.Title] = B, [PageKeys.Survey] = B, [PageKeys.Appraisals] = S, [PageKeys.Approvals] = S,
            },
            // Relocation agent: relocation assistance for displaced owners and occupants.
            [RoleNames.RelocationAgent] = new Dictionary<string, string>
            {
                [PageKeys.Dashboard] = B, [PageKeys.Projects] = B, [PageKeys.Parcels] = B, [PageKeys.Parties] = S,
            },
            // Configures the system: full access to every page, including administration.
            [RoleNames.Admin] = PageKeys.All.ToDictionary(k => k, _ => P),
        };

    private static readonly IReadOnlyDictionary<string, string> RoleDescriptions = new Dictionary<string, string>
    {
        [RoleNames.ProjectManager] = "Creates projects and parcels, sets budget, team and milestones; approves at project level.",
        [RoleNames.TitleResearcher] = "Researches title, identifies liens and encumbrances, and links deeds and title reports to the parcel.",
        [RoleNames.Designer] = "Adds and updates parcels and their details from the plans and ROW maps.",
        [RoleNames.DesignApprover] = "Approves parcels for acquisition.",
        [RoleNames.Surveyor] = "Verifies right-of-way limits and interest type, and records field data.",
        [RoleNames.AppraisalApprover] = "Reviews appraisals and approves just compensation.",
        [RoleNames.RelocationAgent] = "Handles relocation assistance, eligibility and benefits for displacees.",
        [RoleNames.Appraiser] = "Scopes, performs and reviews appraisals and establishes just compensation.",
        [RoleNames.Negotiator] = "Prepares and delivers offers, negotiates settlements, and executes agreements.",
        [RoleNames.Finance] = "Reserves funds, processes payments and tracks expenditure.",
        [RoleNames.Admin] = "Configures rules, workflows, statuses, approvals, roles and users.",
    };

    /// <summary>The template for a role on a page, or null when the role has no access to it.</summary>
    public static string? TemplateFor(string role, string pageKey) =>
        pageKey == PageKeys.Library && Matrix.ContainsKey(role) ? (role == RoleNames.Admin ? P : B) :
        Matrix.TryGetValue(role, out var pages) && pages.TryGetValue(pageKey, out var t) ? t : null;

    public static string DescriptionFor(string role) =>
        RoleDescriptions.TryGetValue(role, out var d) ? d : string.Empty;
}
