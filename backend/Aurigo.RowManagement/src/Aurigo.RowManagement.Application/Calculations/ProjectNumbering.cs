using System.Text.RegularExpressions;

namespace Aurigo.RowManagement.Application.Calculations;

/// <summary>
/// Pure rules for the configurable project-number format (ROW-001). A pattern mixes literal text
/// (letters, digits, '-', '_', '/', '.') with the tokens {FY} (4-digit fiscal year), {YY} (2-digit) and
/// exactly one {SEQ:n} (sequence, zero-padded to n digits, 1 to 9). The sequence restarts each fiscal year
/// when the pattern contains a year token, otherwise it runs once for the tenant.
/// </summary>
public static class ProjectNumbering
{
    public const int MaxLength = 50;
    public const string AllScope = "all";

    private static readonly Regex Token = new(@"\{(?<name>[A-Z]+)(?::(?<arg>\d+))?\}", RegexOptions.Compiled);
    private static readonly Regex Literal = new(@"^[A-Za-z0-9\-_/.]*$", RegexOptions.Compiled);

    /// <summary>Problems that make a pattern unusable; empty when valid.</summary>
    public static IReadOnlyList<string> Validate(string? pattern)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(pattern)) return new[] { "A pattern is required." };

        var seq = 0;
        foreach (Match m in Token.Matches(pattern))
        {
            switch (m.Groups["name"].Value)
            {
                case "FY" or "YY" when !m.Groups["arg"].Success: break;
                case "SEQ" when m.Groups["arg"].Success:
                    seq++;
                    var width = int.Parse(m.Groups["arg"].Value);
                    if (width is < 1 or > 9) errors.Add("{SEQ:n} width must be between 1 and 9.");
                    break;
                default:
                    errors.Add($"Unknown or malformed token {m.Value}. Use {{FY}}, {{YY}} and {{SEQ:n}}.");
                    break;
            }
        }
        if (seq != 1) errors.Add("The pattern must contain exactly one {SEQ:n} token.");

        var literals = Token.Replace(pattern, string.Empty);
        if (!Literal.IsMatch(literals)) errors.Add("Only letters, digits and - _ / . are allowed outside tokens.");

        if (errors.Count == 0 && Render(pattern, 2000, 1).Length > MaxLength)
            errors.Add($"A rendered number cannot be longer than {MaxLength} characters.");
        return errors;
    }

    /// <summary>Which counter the pattern draws from: the fiscal year when it has a year token, else one shared scope.</summary>
    public static string ScopeKey(string pattern, int fiscalYear) =>
        pattern.Contains("{FY}") || pattern.Contains("{YY}") ? fiscalYear.ToString() : AllScope;

    /// <summary>The number for one sequence value. The pattern must already be valid.</summary>
    public static string Render(string pattern, int fiscalYear, int sequence) =>
        Token.Replace(pattern, m => m.Groups["name"].Value switch
        {
            "FY" => fiscalYear.ToString("D4"),
            "YY" => (fiscalYear % 100).ToString("D2"),
            _ => sequence.ToString("D" + m.Groups["arg"].Value),
        });

    /// <summary>
    /// The fiscal year a date falls in. A year starting in January is the calendar year; otherwise the fiscal
    /// year is named for the calendar year it ends in (a year starting 1 October 2025 is FY2026).
    /// </summary>
    public static int FiscalYearOf(DateOnly date, int startMonth) =>
        startMonth <= 1 || date.Month < startMonth ? date.Year : date.Year + 1;
}
