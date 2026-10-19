namespace Aurigo.RowManagement.Domain.Common;

/// <summary>
/// The default ROW roles (Phase 1.5: the eleven in "Next requirements"; derived from the swimlanes in
/// vault/diagrams/Diagram00_Final.md). Agencies may add their own roles; these are the ones the product seeds.
/// </summary>
public static class RoleNames
{
    public const string ProjectManager = "ProjectManager";
    public const string Designer = "Designer";
    public const string DesignApprover = "DesignApprover";
    public const string Surveyor = "Surveyor";
    public const string TitleResearcher = "TitleResearcher";
    public const string Appraiser = "Appraiser";
    public const string AppraisalApprover = "AppraisalApprover";
    public const string Negotiator = "Negotiator";
    public const string RelocationAgent = "RelocationAgent";
    public const string Finance = "Finance";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = new[]
    {
        ProjectManager, Designer, DesignApprover, Surveyor, TitleResearcher, Appraiser,
        AppraisalApprover, Negotiator, RelocationAgent, Finance, Admin,
    };

    public static bool IsKnown(string? role) =>
        role is not null && All.Contains(role, StringComparer.Ordinal);

    /// <summary>The seeded sign-in for each default role: (username, display name, role). Password is set by the demo seed only.</summary>
    public static readonly IReadOnlyList<(string Username, string FirstName, string LastName, string Role)> DemoLogins = new[]
    {
        ("rowprojectmanager", "ROW", "Project Manager", ProjectManager),
        ("rowdesign", "ROW", "Designer", Designer),
        ("rowdesignapprover", "ROW", "Design Approver", DesignApprover),
        ("surveyor", "Surveyor", "User", Surveyor),
        ("titleresearcher", "Title", "Researcher", TitleResearcher),
        ("appraiser", "Appraiser", "User", Appraiser),
        ("appraisalapprover", "Appraisal", "Approver", AppraisalApprover),
        ("rownegotiator", "ROW", "Negotiator", Negotiator),
        ("relocationagent", "Relocation", "Agent", RelocationAgent),
        ("rowfinance", "ROW", "Finance", Finance),
        ("admin", "Admin", "User", Admin),
    };
}
