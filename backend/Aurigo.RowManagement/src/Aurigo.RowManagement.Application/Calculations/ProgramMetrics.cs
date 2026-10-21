namespace Aurigo.RowManagement.Application.Calculations;

// ── The ROW Director's numbers (ADR 0024): how long parcels sit in each step, which team that is, and how healthy a project is ──
// Stateless: the caller passes `now` / `today`. No database, clock or DI in here.

/// <summary>One recorded move along the workflow (a ParcelStepEvent).</summary>
public sealed record StepMove(string? FromStepKey, string ToStepKey, DateTime At);

/// <summary>A parcel's life in the workflow: when it was set up, the step it is on, and every move it has made.</summary>
public sealed record ParcelStepHistory(Guid ParcelId, string CurrentStepKey, DateTime CreatedAt, IReadOnlyList<StepMove> Moves);

/// <summary>One stay in one step. <see cref="Left"/> is null while the parcel is still there.</summary>
public sealed record StepStay(Guid ParcelId, string StepKey, DateTime Entered, DateTime? Left)
{
    public bool IsOpen => Left is null;
    public double DaysAt(DateTime now) => Math.Max(0, ((Left ?? now) - Entered).TotalDays);
}

public sealed record StageSummary(
    string StepKey, string Team, int ParcelsNow, int CompletedStays, double? AvgDaysCompleted, double? AvgDaysOpen, double? LongestOpenDays, int Stuck);

public sealed record TeamSummary(string Team, int ParcelsNow, int Stays, double? AvgDaysPerStay, double TotalDays, double? LongestOpenDays, int Stuck);

public static class StageTimes
{
    /// <summary>A parcel that has been in one step longer than this is "stuck" (a default; the caller may pass its own).</summary>
    public const int DefaultStuckDays = 30;

    /// <summary>
    /// The team that owns a step. A default mapping of the standard workflow; a step this does not know belongs to "Other".
    /// (Making this editable is a later step; see ADR 0024.)
    /// </summary>
    public static string TeamOf(string stepKey) => stepKey switch
    {
        "setup" or "early_approval" => "Project team",
        "title_survey" => "Title & Survey",
        "appraisal" => "Appraisal",
        "offer" or "negotiation" or "agreement" or "donation_review" or "conveyance_terms" => "Negotiation",
        "closing" => "Closing",
        _ => "Other",
    };

    /// <summary>The stays of one parcel, in order. A step visited twice (a reversal) is two stays.</summary>
    public static IReadOnlyList<StepStay> Stays(ParcelStepHistory h)
    {
        var moves = h.Moves.OrderBy(m => m.At).ToList();
        var stays = new List<StepStay>();
        if (moves.Count == 0)
        {
            stays.Add(new StepStay(h.ParcelId, h.CurrentStepKey, h.CreatedAt, null));
            return stays;
        }
        // Before the first recorded move the parcel was on the step that move left (or, if unknown, the first step it reached).
        stays.Add(new StepStay(h.ParcelId, moves[0].FromStepKey ?? moves[0].ToStepKey, h.CreatedAt, moves[0].At));
        for (var i = 0; i < moves.Count; i++)
            stays.Add(new StepStay(h.ParcelId, moves[i].ToStepKey, moves[i].At, i + 1 < moves.Count ? moves[i + 1].At : null));
        return stays;
    }

    /// <summary>One row per step in <paramref name="stepOrder"/> (and any other step seen), counting where parcels are and how long they stayed.</summary>
    public static IReadOnlyList<StageSummary> Summarize(IEnumerable<ParcelStepHistory> histories, IReadOnlyList<string> stepOrder, DateTime now, int stuckDays = DefaultStuckDays)
    {
        var stays = histories.SelectMany(Stays).ToList();
        var keys = stepOrder.Concat(stays.Select(s => s.StepKey)).Distinct(StringComparer.Ordinal).ToList();
        return keys.Select(key =>
        {
            var inStep = stays.Where(s => s.StepKey == key).ToList();
            var done = inStep.Where(s => !s.IsOpen).Select(s => s.DaysAt(now)).ToList();
            var open = inStep.Where(s => s.IsOpen).Select(s => s.DaysAt(now)).ToList();
            return new StageSummary(key, TeamOf(key), open.Count, done.Count, done.Count == 0 ? null : done.Average(), open.Count == 0 ? null : open.Average(),
                open.Count == 0 ? null : open.Max(), open.Count(d => d > stuckDays));
        }).ToList();
    }

    /// <summary>Teams ranked by how long a parcel stays with them on average, longest first. Open stays count up to <paramref name="now"/>.</summary>
    public static IReadOnlyList<TeamSummary> ByTeam(IEnumerable<ParcelStepHistory> histories, DateTime now, int stuckDays = DefaultStuckDays) =>
        histories.SelectMany(Stays).GroupBy(s => TeamOf(s.StepKey))
            .Select(g =>
            {
                var days = g.Select(s => s.DaysAt(now)).ToList();
                var open = g.Where(s => s.IsOpen).Select(s => s.DaysAt(now)).ToList();
                return new TeamSummary(g.Key, open.Count, days.Count, days.Count == 0 ? null : days.Average(), days.Sum(), open.Count == 0 ? null : open.Max(), open.Count(d => d > stuckDays));
            })
            .OrderByDescending(t => t.AvgDaysPerStay ?? 0).ThenBy(t => t.Team, StringComparer.Ordinal).ToList();
}

public enum ProjectHealthLevel { None = 0, Green = 1, Amber = 2, Red = 3 }

public sealed record ProjectHealthInput(
    int ActiveParcels, int ClosedParcels, int OverdueParcels, int StuckParcels, DateOnly? RowClearDate, DateOnly Today, decimal? EstimateTotal, decimal KnownCost);

public sealed record ProjectHealthResult(ProjectHealthLevel Level, IReadOnlyList<string> Reasons);

/// <summary>
/// Red / amber / green for a project, with the reasons in plain words so nobody has to guess why. Thresholds are named constants,
/// not hidden numbers: change them here and the tests say what moved.
/// </summary>
public static class ProjectHealth
{
    public const int ClearDateWarningDays = 90;
    public const int ClearDateWarningPercentClosed = 75;
    public const int RedOverdueMinimumParcels = 3;
    public const int RedOverduePercent = 25;

    public static ProjectHealthResult Evaluate(ProjectHealthInput p)
    {
        if (p.ActiveParcels == 0) return new ProjectHealthResult(ProjectHealthLevel.None, Array.Empty<string>());
        var red = new List<string>();
        var amber = new List<string>();
        var open = p.ActiveParcels - p.ClosedParcels;
        var percentClosed = p.ClosedParcels * 100.0 / p.ActiveParcels;

        if (p.RowClearDate is { } clear)
        {
            if (clear < p.Today && open > 0) red.Add($"The ROW clear date ({clear:yyyy-MM-dd}) has passed with {open} parcel{(open == 1 ? "" : "s")} not closed.");
            else if (clear >= p.Today && clear <= p.Today.AddDays(ClearDateWarningDays) && percentClosed < ClearDateWarningPercentClosed)
                amber.Add($"The ROW clear date is {clear.DayNumber - p.Today.DayNumber} days away and only {Math.Floor(percentClosed):0}% of parcels are closed.");
        }
        if (p.EstimateTotal is > 0 && p.KnownCost > p.EstimateTotal) red.Add($"Known cost {p.KnownCost:C0} is over the estimate {p.EstimateTotal:C0}.");
        if (p.OverdueParcels > 0)
        {
            var big = p.OverdueParcels >= RedOverdueMinimumParcels && p.OverdueParcels * 100.0 / p.ActiveParcels >= RedOverduePercent;
            (big ? red : amber).Add($"{p.OverdueParcels} parcel{(p.OverdueParcels == 1 ? " has" : "s have")} an overdue deadline.");
        }
        if (p.StuckParcels > 0) amber.Add($"{p.StuckParcels} parcel{(p.StuckParcels == 1 ? " has" : "s have")} been on one step for over {StageTimes.DefaultStuckDays} days.");

        var level = red.Count > 0 ? ProjectHealthLevel.Red : amber.Count > 0 ? ProjectHealthLevel.Amber : ProjectHealthLevel.Green;
        return new ProjectHealthResult(level, red.Concat(amber).ToList());
    }
}
