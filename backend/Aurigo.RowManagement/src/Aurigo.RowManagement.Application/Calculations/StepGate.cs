using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Application.Calculations;

public sealed record WorkflowStepInfo(string Key, string Label, bool RequiresAcquisitionGate);

public enum StepMoveOutcome
{
    /// <summary>The next step in order.</summary>
    Allowed = 1,
    /// <summary>Out of order, permitted because the caller may override and gave a reason.</summary>
    AllowedWithOverride = 2,
    /// <summary>Out of order and the caller may override, but gave no reason.</summary>
    OverrideRequiresReason = 3,
    /// <summary>Out of order and the caller's role may not override.</summary>
    OverrideNotPermitted = 4,
    /// <summary>The step starts offers and negotiations and the project's NEPA / authorization gate is closed. Never overridable.</summary>
    BlockedByAcquisitionGate = 5,
    /// <summary>The parcel is already on that step.</summary>
    AlreadyOnStep = 6,
    /// <summary>The step is not in the parcel's workflow.</summary>
    UnknownStep = 7,
    /// <summary>The agency requires engineer and surveyor plan reviews before a parcel leaves the review step, and one is missing. Never overridable (ROW-239).</summary>
    BlockedByPlanReview = 8,
}

public sealed record StepMoveResult(StepMoveOutcome Outcome, string Message)
{
    public bool Allowed => Outcome is StepMoveOutcome.Allowed or StepMoveOutcome.AllowedWithOverride;
    public bool IsOverride => Outcome == StepMoveOutcome.AllowedWithOverride;
}

/// <summary>
/// Pure rules for ordered step gating (ROW-072). A parcel moves to the next step in order. Skipping ahead, going back or
/// repeating is an override: the caller's role must be allowed to override and a reason is required. Steps that start offers
/// and negotiations are held by the project's acquisition gate, which an override can never bypass (use the project's
/// approved-exception path instead).
/// </summary>
public static class StepGate
{
    /// <summary>Roles that may always override; an administrator can add more per agency.</summary>
    public static readonly IReadOnlyList<string> AlwaysMayOverride = new[] { RoleNames.ProjectManager, RoleNames.Admin };

    public static bool MayOverride(IEnumerable<string> userRoles, IEnumerable<string> extraOverrideRoles)
    {
        var allowed = AlwaysMayOverride.Concat(extraOverrideRoles).ToHashSet(StringComparer.Ordinal);
        return userRoles.Any(allowed.Contains);
    }

    /// <summary>The key of the step after <paramref name="currentKey"/>, or null at the last step or for an unknown key.</summary>
    public static string? NextStepKey(IReadOnlyList<WorkflowStepInfo> steps, string currentKey)
    {
        var i = IndexOf(steps, currentKey);
        return i >= 0 && i + 1 < steps.Count ? steps[i + 1].Key : null;
    }

    public static StepMoveResult CanAdvance(
        IReadOnlyList<WorkflowStepInfo> steps, string currentKey, string targetKey,
        bool acquisitionGateOpen, bool mayOverride, string? overrideReason, string? missingPlanReviews = null)
    {
        var from = IndexOf(steps, currentKey);
        var to = IndexOf(steps, targetKey);
        if (to < 0) return new(StepMoveOutcome.UnknownStep, $"'{targetKey}' is not a step in this workflow.");
        if (from < 0) return new(StepMoveOutcome.UnknownStep, $"The parcel's current step '{currentKey}' is not in this workflow.");
        if (to == from) return new(StepMoveOutcome.AlreadyOnStep, $"The parcel is already on '{steps[to].Label}'.");

        // Forward moves into offer/negotiation steps need the project cleared to acquire. Going back is never held.
        if (to > from && steps.Skip(from + 1).Take(to - from).Any(s => s.RequiresAcquisitionGate) && !acquisitionGateOpen)
            return new(StepMoveOutcome.BlockedByAcquisitionGate,
                "Offers and negotiations are on hold until the project has a NEPA decision and an acquisition authorization, or an approved exception.");

        // The plan-review gate holds a parcel on the review step until the plans are reviewed (ROW-239). An override cannot bypass it.
        if (to > from && missingPlanReviews is not null)
        {
            var reviewAt = IndexOf(steps, PlanReviewGate.StepKey);
            if (reviewAt >= from && reviewAt < to)
                return new(StepMoveOutcome.BlockedByPlanReview, $"The agency requires an approved plan review by the {missingPlanReviews} before this parcel can leave '{steps[reviewAt].Label}'.");
        }

        if (to == from + 1) return new(StepMoveOutcome.Allowed, $"Moved to '{steps[to].Label}'.");

        if (!mayOverride)
            return new(StepMoveOutcome.OverrideNotPermitted, "Steps must be completed in order. Only an administrator or project manager can skip, repeat or reverse a step.");
        if (string.IsNullOrWhiteSpace(overrideReason))
            return new(StepMoveOutcome.OverrideRequiresReason, "A reason is required to move a parcel out of order.");
        return new(StepMoveOutcome.AllowedWithOverride, $"Moved to '{steps[to].Label}' out of order.");
    }

    private static int IndexOf(IReadOnlyList<WorkflowStepInfo> steps, string key)
    {
        for (var i = 0; i < steps.Count; i++)
            if (string.Equals(steps[i].Key, key, StringComparison.Ordinal)) return i;
        return -1;
    }
}
