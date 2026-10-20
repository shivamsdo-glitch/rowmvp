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
    public const string TitleEncumbrances = "titleencumbrances"; // a parcel's Title Encumbrances tab
    public const string TitleClosing = "titleclosing";       // a parcel's Title and Closing tab
    public const string ParcelAppraisal = "parcelappraisal";   // a parcel's Appraisal tab
    public const string ParcelNegotiation = "parcelnegotiation";   // a parcel's Negotiation tab
    public const string ParcelQuestionnaire = "parcelquestionnaire";   // a parcel's Questionnaire tab
    public const string ParcelCondemnation = "parcelcondemnation";   // a parcel's Condemnation tab
    public const string ParcelProperty = "parcelproperty";           // a parcel's Property Management & Disposition tab
    public const string ParcelRelocation = "parcelrelocation";       // a parcel's Relocation tab
    public const string Ownership = "ownership";      // a parcel's Ownership Details tab
    public const string ParcelLocation = "parcellocation"; // a parcel's Parcel Location tab (read-only grid)
    public const string ContactLog = "contactlog";    // a parcel's Contact Log tab
    public const string Easements = "easements";      // a parcel's Easements tab
    public const string Library = "library";          // the Library: everyone reads it (form dropdowns); administrators edit it

    public static readonly IReadOnlyList<string> All = new[]
    {
        Dashboard, Projects, Parcels, Parties, Title, Survey, Appraisals,
        Approvals, Negotiations, Agreements, Closing, Payments, Administration, Library, ContactLog, Easements, Ownership, ParcelLocation, TitleEncumbrances, TitleClosing, ParcelAppraisal, ParcelNegotiation, ParcelQuestionnaire, ParcelCondemnation, ParcelProperty, ParcelRelocation,
    };

    public static bool IsKnown(string? key) => key is not null && All.Contains(key, StringComparer.Ordinal);
}
