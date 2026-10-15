using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Application.Calculations;

/// <summary>Pure rules for rights of entry (ROW-077). The clock is passed in.</summary>
public static class RightOfEntryRules
{
    public const int DefaultExpiringSoonDays = 30;

    /// <summary>The reasons a right of entry's facts cannot be right; empty when they can.</summary>
    public static IReadOnlyList<string> Problems(RightOfEntryStatus status, DateOnly? requestedOn, DateOnly? grantedOn, DateOnly? expiresOn, DateOnly today)
    {
        var problems = new List<string>();
        if (requestedOn is { } r && r > today) problems.Add("The request date cannot be in the future.");
        if (grantedOn is { } g && g > today) problems.Add("The date permission was given cannot be in the future.");
        if (status == RightOfEntryStatus.Granted && grantedOn is null) problems.Add("Enter the date permission was given.");
        if (status is RightOfEntryStatus.Requested or RightOfEntryStatus.Denied && grantedOn is not null) problems.Add("Only a granted right of entry has a date permission was given.");
        if (expiresOn is not null && grantedOn is null && status == RightOfEntryStatus.Granted) problems.Add("Enter the date permission was given.");
        if (expiresOn is { } e && grantedOn is { } g2 && e < g2) problems.Add("Permission cannot end before it was given.");
        if (requestedOn is { } r2 && grantedOn is { } g3 && g3 < r2) problems.Add("Permission cannot be given before it was requested.");
        return problems.Distinct().ToList();
    }

    /// <summary>What to show: a granted right whose end date has passed is "Expired".</summary>
    public static string Effective(RightOfEntryStatus status, DateOnly? expiresOn, DateOnly today) =>
        status == RightOfEntryStatus.Granted && expiresOn is { } e && e < today ? "Expired" : status.ToString();

    /// <summary>A granted right that has not ended but ends within <paramref name="days"/> days (inclusive).</summary>
    public static bool ExpiresSoon(RightOfEntryStatus status, DateOnly? expiresOn, DateOnly today, int days) =>
        status == RightOfEntryStatus.Granted && expiresOn is { } e && e >= today && e <= today.AddDays(days);
}

/// <summary>
/// Pure rules for the engineer and surveyor plan-review gate (ROW-239). When the agency turns it on, a parcel cannot leave the
/// <see cref="StepKey"/> step until each discipline's latest review is an approval.
/// </summary>
public static class PlanReviewGate
{
    /// <summary>The step a parcel cannot leave until its plans are reviewed (every workflow has it). A placeholder the agency confirms.</summary>
    public const string StepKey = "title_survey";

    public sealed record ReviewFact(ReviewDiscipline Discipline, ReviewOutcome Outcome, DateOnly ReviewedOn, DateTime RecordedAt);

    /// <summary>The disciplines without an approving latest review; empty when the gate is off or satisfied.</summary>
    public static IReadOnlyList<ReviewDiscipline> Missing(bool gateEnabled, IEnumerable<ReviewFact> reviews)
    {
        if (!gateEnabled) return Array.Empty<ReviewDiscipline>();
        var latest = reviews.GroupBy(r => r.Discipline)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.ReviewedOn).ThenByDescending(r => r.RecordedAt).First());
        return Enum.GetValues<ReviewDiscipline>().Where(d => !latest.TryGetValue(d, out var r) || r.Outcome != ReviewOutcome.Approved).ToList();
    }

    public static string Describe(IEnumerable<ReviewDiscipline> missing) =>
        string.Join(" and ", missing.Select(d => d.ToString().ToLowerInvariant()));
}

/// <summary>Pure rules for the two-step routes: staff reports and acquisition requests (ROW-080, 240).</summary>
public static class ApprovalRoute
{
    /// <summary>The person who put something up for approval cannot approve or reject it themselves.</summary>
    public static bool IsOwnSubmission(Guid? submittedBy, Guid decider) => submittedBy is { } s && s == decider;

    public static bool CanEdit(ReportStatus status) => status is ReportStatus.Draft or ReportStatus.Rejected;
    public static bool CanSubmit(ReportStatus status) => status is ReportStatus.Draft or ReportStatus.Rejected;
    public static bool CanDecide(ReportStatus status) => status == ReportStatus.Submitted;
    public static bool CanDelete(ReportStatus status) => status is ReportStatus.Draft or ReportStatus.Rejected;
}

public sealed record PlanMilestone(string Name, DateOnly? DueOn, DateOnly? CompletedOn);
public sealed record PlanParcelInput(Guid Id, string Number, string? Address, decimal AreaSqFt, string StatusName, string StepLabel, IReadOnlyList<PlanMilestone> Milestones);
public sealed record PlanBudgetYear(int FiscalYear, decimal Amount);

public sealed record PlanParcelRow(
    Guid Id, string Number, string? Address, decimal AreaSqFt, string StatusName, string StepLabel,
    string? NextMilestone, DateOnly? NextDueOn, bool IsOverdue, DateOnly? PlannedCloseOn, bool IsClosed, int MilestonesDone, int MilestonesTotal);

public sealed record PlanBudget(decimal TotalBudget, decimal? CurrentEstimate, decimal AuthorizedTotal, decimal? Variance, bool OverBudget);

public sealed record PlanSummary(
    int ParcelCount, decimal AreaSqFt, int ClosedCount, int OverdueCount, DateOnly? FirstDueOn, DateOnly? LastPlannedCloseOn,
    PlanBudget Budget, IReadOnlyList<PlanParcelRow> Parcels);

/// <summary>
/// Pure calculation of the ROW acquisition plan and schedule (ROW-241): each parcel's timeline from its milestone dates, the
/// project's span, and the budget against the current estimate. Nothing here is stored; it is always computed from the parcels.
/// </summary>
public static class AcquisitionPlan
{
    public static PlanSummary Build(
        IReadOnlyList<PlanParcelInput> parcels, IReadOnlyList<PlanBudgetYear> budgets, decimal? currentEstimate, decimal authorizedTotal, DateOnly today)
    {
        var rows = parcels.OrderBy(p => p.Number, StringComparer.OrdinalIgnoreCase).Select(p =>
        {
            var open = p.Milestones.Where(m => m.CompletedOn is null && m.DueOn is not null).OrderBy(m => m.DueOn).ThenBy(m => m.Name, StringComparer.Ordinal).FirstOrDefault();
            var closing = p.Milestones.FirstOrDefault(m => m.Name == MilestoneNames.Closed);
            var closed = closing?.CompletedOn is not null;
            return new PlanParcelRow(
                p.Id, p.Number, p.Address, p.AreaSqFt, p.StatusName, p.StepLabel,
                open?.Name, open?.DueOn, open?.DueOn is { } d && d < today, closing?.CompletedOn ?? closing?.DueOn, closed,
                p.Milestones.Count(m => m.CompletedOn is not null), p.Milestones.Count);
        }).ToList();

        var totalBudget = budgets.Sum(b => b.Amount);
        decimal? variance = totalBudget > 0 && currentEstimate is { } est ? totalBudget - est : null;
        var budget = new PlanBudget(totalBudget, currentEstimate, authorizedTotal, variance, variance is < 0);

        return new PlanSummary(
            rows.Count, rows.Sum(r => r.AreaSqFt), rows.Count(r => r.IsClosed), rows.Count(r => r.IsOverdue),
            rows.Where(r => r.NextDueOn is not null).Select(r => r.NextDueOn).Min(),
            rows.Where(r => r.PlannedCloseOn is not null).Select(r => r.PlannedCloseOn).Max(), budget, rows);
    }
}
