namespace Aurigo.RowManagement.IntegrationTests.Support;

/// <summary>
/// THE SPEC: what each role may do, written out by hand from vault/acceptance/*.md and the Diagram 00
/// swimlanes. Deliberately a literal table, not derived from DefaultRolePermissions, so a change to the
/// production defaults (or a typo in them) fails tests instead of silently redefining the truth.
/// B = Basic (read) · U = read and update only · S = Standard (create/read/update) · P = Pro (create/read/update/delete) · '-' = no access.
/// The same table is published to vault/acceptance/permission-matrix.json for the frontend tests.
/// </summary>
public static class Spec
{
    public static readonly string[] Roles =
    {
        "ProjectManager", "Designer", "DesignApprover", "Surveyor", "TitleResearcher", "Appraiser",
        "AppraisalApprover", "Negotiator", "RelocationAgent", "Finance", "Admin",
    };

    public static readonly string[] Pages =
    {
        "dashboard", "projects", "parcels", "parties", "title", "survey", "appraisals",
        "approvals", "negotiations", "agreements", "closing", "payments", "administration", "library", "contactlog", "easements", "ownership", "parcellocation", "titleencumbrances", "titleclosing", "parcelappraisal", "parcelnegotiation",
    };

    //                                  dash proj parc part titl surv appr aprv negt agrm clos paym admn libr cont ease owne loca tenc tcls appr2 negn
    private static readonly Dictionary<string, string> Rows = new()
    {
        ["ProjectManager"] = "B P P P B B B S B B B B - B B B B B B B B B",
        ["Designer"]       = "B B P P - - - - - - - - - B S S P P B B B B",
        ["DesignApprover"] = "B B U B - - - S - - - - - B B B B B B B B B",
        ["Surveyor"]       = "B B B B - P - - - - - - - B S P B S B B B B",
        ["AppraisalApprover"] = "B B B B B B S S - - - - - B B B B B B B U B",
        ["RelocationAgent"] = "B B B S - - - - - - - - - B P B B B B B B B",
        ["TitleResearcher"]  = "B B B B P P B - - - - - - B S B U B P S B B",
        ["Appraiser"]      = "B B B B B B P S - - - - - B S B B B B B P B",
        ["Negotiator"]     = "B B B P B - B B P P S S - B P B S B B S B P",
        ["Finance"]        = "B B B B - - - S B B B P - B B B B B B B B B",
        ["Admin"]          = "P P P P P P P P P P P P P P P P P P P P P P",
    };

    public static char Level(string role, string page) => Rows[role].Split(' ')[Array.IndexOf(Pages, page)][0];

    public static bool Can(string role, string page, string action) => Level(role, page) switch
    {
        'P' => true,
        'S' => action != "delete",
        'U' => action is "read" or "update",
        'B' => action == "read",
        _ => false,
    };

    /// <summary>Entity types a file can be attached to, and the page whose permissions govern them.</summary>
    public static readonly IReadOnlyDictionary<string, string> AttachmentPage = new Dictionary<string, string>
    {
        ["Project"] = "projects", ["Parcel"] = "parcels", ["Party"] = "parties", ["TitleRecord"] = "title",
        ["SurveyRequest"] = "survey", ["Appraisal"] = "appraisals", ["Offer"] = "negotiations",
        ["Agreement"] = "agreements", ["ClosingRecord"] = "closing", ["Payment"] = "payments",
        ["ContactLog"] = "contactlog", ["Easement"] = "easements", ["Ownership"] = "ownership", ["Encumbrance"] = "titleencumbrances", ["TitleClosing"] = "titleclosing", ["ParcelAppraisal"] = "parcelappraisal", ["ParcelNegotiation"] = "parcelnegotiation",
    };

    public static IEnumerable<object[]> RoleAndEntityType() =>
        from r in Roles from t in AttachmentPage.Keys select new object[] { r, t };

    public static IEnumerable<object[]> EachRole() => Roles.Select(r => new object[] { r });

    public static IEnumerable<object[]> RoleAndPage() =>
        from r in Roles from p in Pages select new object[] { r, p };
}
