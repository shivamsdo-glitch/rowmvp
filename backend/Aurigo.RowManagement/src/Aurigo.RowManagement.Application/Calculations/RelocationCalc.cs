using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Application.Calculations;

/// <summary>One relocation as the parcel-level rules see it: which ownership interest it is for and whether it is complete.</summary>
public sealed record RelocationProgressInput(Guid? PartyLinkId, DateOnly? RelocationComplete);

/// <summary>Where a parcel stands on relocating its displaced ownership interests (ADR 0026).</summary>
public sealed record RelocationProgress(int Flagged, int Completed)
{
    /// <summary>True when at least one interest must be relocated and every such interest is done. A parcel with no displaced interests never qualifies (the rule does not apply).</summary>
    public bool AllComplete => Flagged > 0 && Completed == Flagged;
}

/// <summary>Pure rules for relocation (Diagram 5). No database, clock or DI: pass <c>today</c> in.</summary>
public static class RelocationCalc
{
    /// <summary>A displacee has this many months after displacement to claim benefits (ATP 6.5d; ROW-095). The agency can waive it for good cause.</summary>
    public const int ClaimMonths = 18;

    /// <summary>
    /// Counts the displaced ownership interests and how many have a relocation with a Relocation complete date. An interest counts once even
    /// when it has several relocation records (it is complete when any one is complete).
    /// </summary>
    public static RelocationProgress Progress(IReadOnlyCollection<Guid> displacedInterestIds, IEnumerable<RelocationProgressInput> relocations)
    {
        var done = relocations.Where(r => r.PartyLinkId is not null && r.RelocationComplete is not null).Select(r => r.PartyLinkId!.Value).ToHashSet();
        return new RelocationProgress(displacedInterestIds.Count, displacedInterestIds.Count(done.Contains));
    }

    /// <summary>The last day to claim: 18 months after the displacee vacated; null until they have.</summary>
    public static DateOnly? ClaimDeadline(DateOnly? vacated) => vacated?.AddMonths(ClaimMonths);

    /// <summary>The claim period is running out: it ends within <paramref name="warnDays"/> days (or already has) and the case is not closed out.</summary>
    public static bool ClaimExpiring(DateOnly? vacated, DateOnly? completed, DateOnly today, int warnDays) =>
        completed is null && ClaimDeadline(vacated) is { } deadline && deadline.DayNumber - today.DayNumber <= warnDays;

    /// <summary>
    /// Where the case is, from the dates recorded. Order follows Diagram 5; the first step not yet dated is the one waiting.
    /// </summary>
    public static string Stage(DateOnly? gin, DateOnly? interview, DateOnly? eligibilityNotice, DateOnly? housingStudy, DateOnly? notice90, DateOnly? notice30,
        DateOnly? vacated, DateOnly? completed, bool ineligible)
    {
        if (completed is not null) return "Complete";
        if (ineligible) return "Ineligible";
        if (vacated is not null) return "Vacated: close out";
        if (notice30 is not null) return "Awaiting move";
        if (notice90 is not null) return "90-day notice given";
        if (housingStudy is not null) return "Housing study done";
        if (eligibilityNotice is not null) return "Eligible";
        if (interview is not null) return "Interviewed";
        if (gin is not null) return "GIN sent";
        return "Not started";
    }

    /// <summary>The placeholder approval chain a tenant uses until it saves its own bands. Placeholders, not law (ADR 0026).</summary>
    public static ApprovalChainConfig DefaultPaymentChain { get; } = new(new[]
    {
        new ApprovalBand(0m, 25_000m, new[] { RoleNames.ProjectManager }),
        new ApprovalBand(25_000m, null, new[] { RoleNames.ProjectManager, RoleNames.Finance }),
    });

    public static IReadOnlyList<string> ParseRoles(string roles) => roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
