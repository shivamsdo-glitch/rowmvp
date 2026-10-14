using System.Text.RegularExpressions;

namespace Aurigo.RowManagement.Application.Calculations;

public sealed record SplitPlan(IReadOnlyList<decimal> PartAreas, decimal RemnantAreaSqFt, IReadOnlyList<string> Numbers, string? RemnantNumber);

/// <summary>Pure rules for splitting and combining parcels and numbering new ones (ROW-007).</summary>
public static class ParcelSplit
{
    public const int MaxParts = 25;

    /// <summary>Suffix letters for split parts. R is skipped because "-R" is the remnant.</summary>
    private const string Letters = "ABCDEFGHIJKLMNOPQSTUVWXYZ";

    /// <summary>Problems that make the requested areas unusable; empty when they are valid.</summary>
    public static IReadOnlyList<string> Validate(decimal sourceAreaSqFt, IReadOnlyList<decimal> partAreas)
    {
        var errors = new List<string>();
        if (partAreas.Count == 0) errors.Add("At least one part is required.");
        if (partAreas.Count > MaxParts) errors.Add($"A parcel can be split into at most {MaxParts} parts.");
        if (partAreas.Any(a => a <= 0m)) errors.Add("Every part needs an area greater than zero.");
        if (errors.Count == 0 && partAreas.Sum() > sourceAreaSqFt)
            errors.Add($"The parts total {partAreas.Sum():N2} sq ft, more than the parcel's {sourceAreaSqFt:N2} sq ft.");
        return errors;
    }

    /// <summary>
    /// Plans a split: the numbers of the parts ("12-A", "12-B", ...) and, when the parts do not use all of the area, a remnant
    /// parcel ("12-R") holding the rest. The areas must already be valid.
    /// </summary>
    public static SplitPlan Plan(string sourceNumber, decimal sourceAreaSqFt, IReadOnlyList<decimal> partAreas)
    {
        var remnant = sourceAreaSqFt - partAreas.Sum();
        var numbers = Enumerable.Range(0, partAreas.Count).Select(i => $"{sourceNumber}-{Letters[i]}").ToList();
        return new SplitPlan(partAreas, remnant, numbers, remnant > 0m ? $"{sourceNumber}-R" : null);
    }

    /// <summary>The next unused plain number for a project: the highest all-digit number plus one, at least three digits ("007").</summary>
    public static string NextNumber(IEnumerable<string> existingNumbers)
    {
        var highest = 0;
        foreach (var n in existingNumbers)
            if (Regex.IsMatch(n, @"^\d{1,9}$") && int.Parse(n) > highest) highest = int.Parse(n);
        return (highest + 1).ToString("D3");
    }
}
