using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Application.Documents;

/// <summary>
/// The kinds of ROW entity a file can be attached to, and which page's permissions govern them.
/// A module that wants attachments adds its entity type here; it does not write its own upload flow.
/// Reading needs Read on the page, attaching needs Update, removing needs Delete.
/// </summary>
public static class AttachmentTargets
{
    private static readonly IReadOnlyDictionary<string, string> PageByType = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Project"] = PageKeys.Projects,
        ["Parcel"] = PageKeys.Parcels,
        ["Party"] = PageKeys.Parties,
        ["TitleRecord"] = PageKeys.Title,
        ["SurveyRequest"] = PageKeys.Survey,
        ["Appraisal"] = PageKeys.Appraisals,
        ["Offer"] = PageKeys.Negotiations,
        ["Agreement"] = PageKeys.Agreements,
        ["ClosingRecord"] = PageKeys.Closing,
        ["Payment"] = PageKeys.Payments,
        ["ContactLog"] = PageKeys.ContactLog,
        ["Easement"] = PageKeys.Easements,
    };

    public static IReadOnlyCollection<string> EntityTypes => PageByType.Keys.ToArray();

    public static bool IsKnown(string? entityType) => entityType is not null && PageByType.ContainsKey(entityType);

    /// <summary>The page governing the type, or null when the type is not registered.</summary>
    public static string? PageFor(string? entityType) =>
        entityType is not null && PageByType.TryGetValue(entityType, out var page) ? page : null;
}
