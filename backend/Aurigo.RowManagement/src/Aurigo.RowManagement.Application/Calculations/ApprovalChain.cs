using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Application.Calculations;

/// <summary>
/// One dollar band of an approval chain. <see cref="MinAmount"/> is inclusive, <see cref="MaxAmount"/> is
/// exclusive (null = no upper limit). <see cref="RequiredRoles"/> are the sign-offs in order.
/// </summary>
public sealed record ApprovalBand(decimal MinAmount, decimal? MaxAmount, IReadOnlyList<string> RequiredRoles);

/// <summary>Agency-configured thresholds. Always data, never hard-coded amounts (ROW-046, 258, 261, 108).</summary>
public sealed record ApprovalChainConfig(IReadOnlyList<ApprovalBand> Bands);

/// <summary>The sign-offs one amount needs, in order.</summary>
public sealed record ApprovalRequirement(ApprovalBand Band, IReadOnlyList<string> Steps);

/// <summary>Pure rules for configurable, threshold-based approval chains.</summary>
public static class ApprovalChain
{
    /// <summary>Problems that make a config unusable; empty when it is valid.</summary>
    public static IReadOnlyList<string> Validate(ApprovalChainConfig config)
    {
        var errors = new List<string>();
        if (config.Bands.Count == 0) return new[] { "At least one band is required." };

        var ordered = config.Bands.OrderBy(b => b.MinAmount).ToList();
        if (ordered[0].MinAmount != 0m) errors.Add("The first band must start at 0.");
        if (ordered[^1].MaxAmount is not null) errors.Add("The last band must have no upper limit.");

        for (var i = 0; i < ordered.Count; i++)
        {
            var band = ordered[i];
            if (band.MinAmount < 0m) errors.Add($"Band {i + 1}: minimum cannot be negative.");
            if (band.MaxAmount is { } max && max <= band.MinAmount) errors.Add($"Band {i + 1}: maximum must be greater than minimum.");
            if (band.RequiredRoles.Count == 0) errors.Add($"Band {i + 1}: at least one approver role is required.");
            if (band.RequiredRoles.Any(r => !RoleNames.IsKnown(r))) errors.Add($"Band {i + 1}: unknown role.");
            if (band.RequiredRoles.Distinct().Count() != band.RequiredRoles.Count) errors.Add($"Band {i + 1}: a role is listed twice.");

            if (i + 1 < ordered.Count && band.MaxAmount is { } upper)
            {
                var next = ordered[i + 1];
                if (upper > next.MinAmount) errors.Add($"Bands {i + 1} and {i + 2} overlap.");
                else if (upper < next.MinAmount) errors.Add($"There is a gap between bands {i + 1} and {i + 2}.");
            }
        }
        return errors;
    }

    /// <summary>The band that governs the amount and the ordered sign-offs it needs.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The amount is negative.</exception>
    /// <exception cref="InvalidOperationException">No band covers the amount (the config has a gap).</exception>
    public static ApprovalRequirement Evaluate(decimal amount, ApprovalChainConfig config)
    {
        if (amount < 0m) throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");

        var band = config.Bands.FirstOrDefault(b => amount >= b.MinAmount && (b.MaxAmount is null || amount < b.MaxAmount))
                   ?? throw new InvalidOperationException($"No approval band covers {amount}.");
        return new ApprovalRequirement(band, band.RequiredRoles.ToList());
    }

    /// <summary>The next role that still has to sign, or null when every required step is done. Steps are strictly ordered.</summary>
    public static string? NextApprover(ApprovalRequirement requirement, IReadOnlyCollection<string> approvedSoFar)
    {
        foreach (var step in requirement.Steps)
            if (!approvedSoFar.Contains(step)) return step;
        return null;
    }

    public static bool IsComplete(ApprovalRequirement requirement, IReadOnlyCollection<string> approvedSoFar) =>
        NextApprover(requirement, approvedSoFar) is null;

    /// <summary>Whether the user may sign now: one of their roles must be the next step in the chain.</summary>
    public static bool CanApprove(ApprovalRequirement requirement, IReadOnlyCollection<string> approvedSoFar, IEnumerable<string> userRoles) =>
        NextApprover(requirement, approvedSoFar) is { } next && userRoles.Contains(next);
}
