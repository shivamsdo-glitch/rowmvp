namespace Aurigo.RowManagement.Domain.Common;

/// <summary>
/// Labels for a role's access on one page. They are shorthand for a set of CRUD flags
/// (see Application/Calculations/PermissionTemplateCalculator); "Custom" means the flags were edited by hand.
/// </summary>
public static class PermissionTemplates
{
    public const string Basic = "Basic";       // read only
    public const string Standard = "Standard"; // create, read, update
    public const string Pro = "Pro";           // create, read, update, delete
    public const string Custom = "Custom";     // any other combination, including no access

    public static readonly IReadOnlyList<string> All = new[] { Basic, Standard, Pro, Custom };
}
