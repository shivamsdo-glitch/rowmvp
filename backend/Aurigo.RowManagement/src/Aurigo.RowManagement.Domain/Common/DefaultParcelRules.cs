using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Domain.Common;

/// <summary>Milestones every parcel starts with. Deadline rules refer to milestones by these names.</summary>
public static class MilestoneNames
{
    public const string ParcelSetUp = "Parcel set up";
    public const string NoticeToOwner = "Notice to owner";
    public const string OfferDelivered = "Offer delivered";
    public const string AgreementSigned = "Agreement signed";
    public const string Closed = "Closed";

    public static readonly IReadOnlyList<string> Standard = new[] { ParcelSetUp, NoticeToOwner, OfferDelivered, AgreementSigned, Closed };
}

public sealed record DefaultStatus(string Name, string Color, bool IsInitial);
public sealed record DefaultStep(string Key, string Label, bool RequiresAcquisitionGate);
public sealed record DefaultDeadlineRule(string Name, string Trigger, string Target, int Days);

/// <summary>
/// Out-of-the-box parcel rules for a new agency. Every item is agency data that an administrator can change; none is a
/// legal requirement. The deadline periods in particular are PLACEHOLDERS until the agency confirms them
/// (vault/README_Final.md, "Yours to verify").
/// </summary>
public static class DefaultParcelRules
{
    /// <summary>
    /// The parcel status list a new agency starts with (the agency's own list: users add, rename and switch values off in the
    /// Library). There are no colours: every status takes one neutral colour. New parcels start as Inactive.
    /// </summary>
    public const string NeutralColor = "#64748b";

    /// <summary>The status a parcel moves to when every displaced ownership interest has finished relocating (ADR 0026). Looked up by name.</summary>
    public const string AcquiredStatus = "Acquired";

    public static readonly IReadOnlyList<DefaultStatus> Statuses = new[]
    {
        "Inactive", "In Design", "Pre-Acquisition", "Appraisal", "Initial Offer", "Final Offer", "In Possession - PUA", "In Possession - CLOSED", "Acquired",
        "In Possession - NOD", "Pending Condemnation", "Petition Filed", "Hearing Scheduled", "Objections Filed", "Deleted",
        "Disposition In Progress", "Disposition Complete",
    }.Select(n => new DefaultStatus(n, NeutralColor, n == "Inactive")).ToArray();

    private static readonly DefaultStep Setup = new("setup", "Parcel set up", false);
    private static readonly DefaultStep TitleSurvey = new("title_survey", "Title & survey", false);
    private static readonly DefaultStep Appraisal = new("appraisal", "Appraisal & just compensation", false);
    private static readonly DefaultStep Offer = new("offer", "Offer", true);
    private static readonly DefaultStep Negotiation = new("negotiation", "Negotiation", true);
    private static readonly DefaultStep Agreement = new("agreement", "Agreement", true);
    private static readonly DefaultStep Closing = new("closing", "Closing", true);

    public static readonly IReadOnlyDictionary<AcquisitionType, IReadOnlyList<DefaultStep>> Workflows =
        new Dictionary<AcquisitionType, IReadOnlyList<DefaultStep>>
        {
            [AcquisitionType.Standard] = new[] { Setup, TitleSurvey, Appraisal, Offer, Negotiation, Agreement, Closing },
            [AcquisitionType.Donation] = new[] { Setup, TitleSurvey, new DefaultStep("donation_review", "Donation review", false), new DefaultStep("agreement", "Agreement", true), Closing },
            [AcquisitionType.DeveloperConveyance] = new[] { Setup, TitleSurvey, new DefaultStep("conveyance_terms", "Conveyance terms", false), new DefaultStep("agreement", "Agreement", true), Closing },
            [AcquisitionType.Early] = new[] { Setup, new DefaultStep("early_approval", "Early acquisition approval", false), TitleSurvey, Appraisal, Offer, Agreement, Closing },
        };

    public static readonly IReadOnlyList<DefaultDeadlineRule> DeadlineRules = new DefaultDeadlineRule[]
    {
        new("Offer within 90 days of the Notice to Owner", MilestoneNames.NoticeToOwner, MilestoneNames.OfferDelivered, 90),
        new("Close within 24 months of parcel set up (agency parcel clock)", MilestoneNames.ParcelSetUp, MilestoneNames.Closed, 730),
    };
}
