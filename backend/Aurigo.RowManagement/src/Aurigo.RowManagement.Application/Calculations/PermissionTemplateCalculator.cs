using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Application.Calculations;

/// <summary>What a role may do on one page.</summary>
public readonly record struct PermissionFlags(bool Create, bool Read, bool Update, bool Delete)
{
    public static readonly PermissionFlags None = new(false, false, false, false);

    public bool Allows(PermissionAction action) => action switch
    {
        PermissionAction.Create => Create,
        PermissionAction.Read => Read,
        PermissionAction.Update => Update,
        PermissionAction.Delete => Delete,
        _ => false,
    };
}

/// <summary>
/// Pure rules for page permissions: what a template label means, which label describes a set of flags,
/// and how several roles' grants on one page combine.
/// </summary>
public static class PermissionTemplateCalculator
{
    /// <summary>Basic = read; Standard = create, read, update; Pro = all four. Anything else grants nothing.</summary>
    public static PermissionFlags FlagsFor(string? template) => template switch
    {
        PermissionTemplates.Basic => new PermissionFlags(false, true, false, false),
        PermissionTemplates.Standard => new PermissionFlags(true, true, true, false),
        PermissionTemplates.Pro => new PermissionFlags(true, true, true, true),
        // A default may also be written as the letters of the boxes ticked: "RU" = read and update.
        { Length: > 0 } code when code.All(c => "CRUD".Contains(c)) =>
            new PermissionFlags(code.Contains('C'), code.Contains('R'), code.Contains('U'), code.Contains('D')),
        _ => PermissionFlags.None,
    };

    /// <summary>The label that exactly describes the flags, or Custom when none does (including no access).</summary>
    public static string TemplateFor(PermissionFlags flags)
    {
        foreach (var template in new[] { PermissionTemplates.Basic, PermissionTemplates.Standard, PermissionTemplates.Pro })
            if (FlagsFor(template) == flags) return template;
        return PermissionTemplates.Custom;
    }

    /// <summary>A user with several roles gets the union of what each role allows on a page.</summary>
    public static PermissionFlags Merge(IEnumerable<PermissionFlags> grants)
    {
        var merged = PermissionFlags.None;
        foreach (var g in grants)
            merged = new PermissionFlags(
                merged.Create || g.Create, merged.Read || g.Read,
                merged.Update || g.Update, merged.Delete || g.Delete);
        return merged;
    }
}
