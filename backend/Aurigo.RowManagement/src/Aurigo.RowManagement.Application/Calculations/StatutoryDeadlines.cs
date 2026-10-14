namespace Aurigo.RowManagement.Application.Calculations;

public sealed record DeadlineRuleInfo(Guid Id, string Name, string TriggerMilestone, string TargetMilestone, int Days);

public sealed record MilestoneInfo(string Name, DateOnly? DueOn, DateOnly? CompletedOn);

public enum DeadlineStatus
{
    /// <summary>The trigger milestone is not complete, so the clock has not started.</summary>
    NotStarted = 1,
    /// <summary>The clock is running and the deadline is more than the warning window away.</summary>
    Pending = 2,
    /// <summary>The deadline is today or within the warning window.</summary>
    DueSoon = 3,
    /// <summary>The deadline has passed and the target milestone is not complete (a delay flag).</summary>
    Overdue = 4,
    /// <summary>The target milestone was completed on or before the deadline.</summary>
    Met = 5,
    /// <summary>The target milestone was completed after the deadline.</summary>
    MetLate = 6,
}

public sealed record DeadlineResult(
    DeadlineRuleInfo Rule, DateOnly? TriggerDate, DateOnly? DueOn, DeadlineStatus Status, int? DaysRemaining);

/// <summary>
/// Pure rules for statutory and agency timelines (ROW-073, 074). A rule says: the target milestone is due <c>Days</c> days
/// after the trigger milestone is completed. Rules are agency data; nothing here knows a legal period. The caller passes
/// today's date.
/// </summary>
public static class StatutoryDeadlines
{
    public const int DefaultDueSoonDays = 7;

    public static IReadOnlyList<DeadlineResult> Compute(
        IEnumerable<DeadlineRuleInfo> rules, IReadOnlyCollection<MilestoneInfo> milestones, DateOnly today, int dueSoonDays = DefaultDueSoonDays) =>
        rules.Select(r => Compute(r, milestones, today, dueSoonDays)).ToList();

    public static DeadlineResult Compute(
        DeadlineRuleInfo rule, IReadOnlyCollection<MilestoneInfo> milestones, DateOnly today, int dueSoonDays = DefaultDueSoonDays)
    {
        var trigger = Find(milestones, rule.TriggerMilestone)?.CompletedOn;
        if (trigger is null) return new(rule, null, null, DeadlineStatus.NotStarted, null);

        var due = trigger.Value.AddDays(rule.Days);
        var completed = Find(milestones, rule.TargetMilestone)?.CompletedOn;
        if (completed is { } done)
            return new(rule, trigger, due, done <= due ? DeadlineStatus.Met : DeadlineStatus.MetLate, null);

        var remaining = due.DayNumber - today.DayNumber;
        var status = remaining < 0 ? DeadlineStatus.Overdue : remaining <= dueSoonDays ? DeadlineStatus.DueSoon : DeadlineStatus.Pending;
        return new(rule, trigger, due, status, remaining);
    }

    /// <summary>The results that need someone's attention now: overdue first (most overdue at the top), then due soon (soonest first).</summary>
    public static IReadOnlyList<DeadlineResult> NeedingAttention(IEnumerable<DeadlineResult> results) =>
        results.Where(r => r.Status is DeadlineStatus.Overdue or DeadlineStatus.DueSoon)
            .OrderBy(r => r.Status == DeadlineStatus.Overdue ? 0 : 1)
            .ThenBy(r => r.DueOn)
            .ToList();

    private static MilestoneInfo? Find(IReadOnlyCollection<MilestoneInfo> milestones, string name) =>
        milestones.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
}
