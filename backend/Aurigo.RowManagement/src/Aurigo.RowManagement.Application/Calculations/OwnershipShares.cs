namespace Aurigo.RowManagement.Application.Calculations;

/// <summary>
/// Pure rules for ownership shares (ROW-014). Undivided interests are held in shares of the whole, so the shares given to
/// owners of one kind of interest on a tract or parcel cannot add up to more than 100 percent.
/// </summary>
public static class OwnershipShares
{
    public const decimal Whole = 100m;
    public const int MaxDecimals = 4;

    /// <summary>Problems with one share on its own: it must be above zero, at most the whole, and no finer than four decimals.</summary>
    public static IReadOnlyList<string> CheckShare(decimal? share)
    {
        if (share is null) return Array.Empty<string>();
        var problems = new List<string>();
        if (share <= 0 || share > Whole) problems.Add("A share must be more than 0 and at most 100 percent.");
        else if (decimal.Round(share.Value, MaxDecimals) != share.Value) problems.Add($"A share can have at most {MaxDecimals} decimal places.");
        return problems;
    }

    /// <summary>The share that is still unclaimed after <paramref name="otherShares"/> (never below zero).</summary>
    public static decimal Remaining(IEnumerable<decimal?> otherShares) => Math.Max(0m, Whole - otherShares.Sum(s => s ?? 0m));

    /// <summary>True when giving <paramref name="share"/> alongside <paramref name="otherShares"/> would claim more than the whole.</summary>
    public static bool Exceeds(IEnumerable<decimal?> otherShares, decimal? share) => (share ?? 0m) > Remaining(otherShares);
}
