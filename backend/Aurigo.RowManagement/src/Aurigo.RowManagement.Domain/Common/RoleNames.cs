namespace Aurigo.RowManagement.Domain.Common;

/// <summary>
/// The six ROW roles, derived from the swimlanes in vault/diagrams/Diagram00_Final.md.
/// Use the constants in <c>[Authorize(Roles = ...)]</c>.
/// </summary>
public static class RoleNames
{
    public const string ProjectManager = "ProjectManager";
    public const string TitleExaminer = "TitleExaminer";
    public const string Appraiser = "Appraiser";
    public const string Negotiator = "Negotiator";
    public const string Finance = "Finance";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All =
        new[] { ProjectManager, TitleExaminer, Appraiser, Negotiator, Finance, Admin };

    public static bool IsKnown(string? role) =>
        role is not null && All.Contains(role, StringComparer.Ordinal);
}
