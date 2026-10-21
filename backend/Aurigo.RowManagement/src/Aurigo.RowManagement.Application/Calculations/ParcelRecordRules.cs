namespace Aurigo.RowManagement.Application.Calculations;

/// <summary>Pure rules for a parcel's team, diary and notes (ROW-019..021). The clock is passed in.</summary>
public static class ParcelRecordRules
{
    public const int MaxSummary = 2000;
    public const int MaxOutcome = 1000;
    public const int MaxNote = 4000;

    /// <summary>The key that makes a name the same person ignoring case, spacing and repeated spaces.</summary>
    public static string NameKey(string name) => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    /// <summary>The name as it is kept: trimmed, with runs of spaces made single.</summary>
    public static string CleanName(string name) => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A diary entry records something that has happened, so it cannot be dated after today where the project is.</summary>
    public static string? DiaryDateProblem(DateOnly occurredOn, DateOnly today)
    {
        if (occurredOn > today) return "A diary entry cannot be dated in the future.";
        if (occurredOn.Year < 1990) return "Enter a valid date.";
        return null;
    }

    /// <summary>A role (a Library agent type) as it reads in a sentence: lower case, "team member" when none is given.</summary>
    public static string DescribeRole(string? role) => string.IsNullOrWhiteSpace(role) ? "team member" : role.Trim().ToLowerInvariant();
}
