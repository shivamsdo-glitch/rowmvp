using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Application.Calculations;

/// <summary>
/// Pure rules for the tax identification numbers a payee gives on a W-9 (ROW-018): what counts as a valid number, and how
/// it is shown. The number itself is only ever handled long enough to validate and protect it; people see the last four digits.
/// </summary>
public static class TaxId
{
    public const int Length = 9;

    /// <summary>Just the digits of what was typed (spaces and hyphens are allowed separators and are dropped).</summary>
    public static string Digits(string? input) => new((input ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    /// <summary>The reasons a number cannot be right for its type; empty when it can.</summary>
    public static IReadOnlyList<string> Validate(TaxIdType type, string? input)
    {
        var problems = new List<string>();
        var raw = input ?? string.Empty;
        if (raw.Any(c => !char.IsAsciiDigit(c) && c != '-' && c != ' '))
        {
            problems.Add("A tax number can contain only digits, spaces and hyphens.");
            return problems;
        }

        var d = Digits(raw);
        if (d.Length != Length)
        {
            problems.Add($"A tax number has {Length} digits.");
            return problems;
        }

        switch (type)
        {
            case TaxIdType.Ssn:
                var area = int.Parse(d[..3]);
                if (area == 0 || area == 666 || area >= 900) problems.Add("That is not a valid Social Security number.");
                else if (d[3..5] == "00" || d[5..] == "0000") problems.Add("That is not a valid Social Security number.");
                break;
            case TaxIdType.Itin:
                if (d[0] != '9' || !ItinGroupIsValid(int.Parse(d[3..5]))) problems.Add("That is not a valid Individual Taxpayer Identification Number.");
                break;
            case TaxIdType.Ein:
                if (d[..2] == "00") problems.Add("That is not a valid Employer Identification Number.");
                break;
            default:
                problems.Add("Choose the type of tax number.");
                break;
        }
        return problems;
    }

    public static string Last4(string digits) => digits.Length >= 4 ? digits[^4..] : digits;

    /// <summary>How a number is shown: <c>***-**-1234</c> for a Social Security number or ITIN, <c>**-***1234</c> for an EIN.</summary>
    public static string Mask(TaxIdType type, string last4) => type == TaxIdType.Ein ? $"**-***{last4}" : $"***-**-{last4}";

    // The IRS issues ITINs only with these 4th and 5th digits (50-65, 70-88, 90-92, 94-99).
    private static bool ItinGroupIsValid(int group) => group is (>= 50 and <= 65) or (>= 70 and <= 88) or (>= 90 and <= 92) or (>= 94 and <= 99);
}
