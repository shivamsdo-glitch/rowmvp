namespace Aurigo.RowManagement.Domain.Common;

/// <summary>
/// One key per ROW module page that page-level permissions are granted on. Keys are stable strings
/// (stored in role_page_permissions and used by the web app for navigation), so never rename one.
/// </summary>
public static class PageKeys
{
    public const string Dashboard = "dashboard";
    public const string Projects = "projects";
    public const string Parcels = "parcels";
    public const string Parties = "parties";          // owners, interests, contacts, tenants
    public const string Title = "title";
    public const string Survey = "survey";
    public const string Appraisals = "appraisals";
    public const string Approvals = "approvals";      // approval inbox across modules
    public const string Negotiations = "negotiations";
    public const string Agreements = "agreements";
    public const string Closing = "closing";
    public const string Payments = "payments";
    public const string Administration = "administration";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Dashboard, Projects, Parcels, Parties, Title, Survey, Appraisals,
        Approvals, Negotiations, Agreements, Closing, Payments, Administration,
    };

    public static bool IsKnown(string? key) => key is not null && All.Contains(key, StringComparer.Ordinal);
}
