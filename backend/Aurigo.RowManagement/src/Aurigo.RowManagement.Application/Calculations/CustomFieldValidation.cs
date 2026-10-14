using System.Globalization;
using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Application.Calculations;

public sealed record CustomFieldInfo(string Key, string Label, CustomFieldType Type, IReadOnlyList<string> Options, bool IsRequired, bool IsActive);

public sealed record CustomFieldCheck(IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Pure validation of the values entered for a tenant's custom fields (ROW-011). Values arrive as text and are normalised
/// (trimmed, numbers and booleans in a canonical form, blanks dropped). A value for a field that has since been deactivated
/// is kept only if it is unchanged from what is already stored.
/// </summary>
public static class CustomFieldValidation
{
    public const int MaxTextLength = 500;
    public const int MaxValues = 50;

    public static CustomFieldCheck Validate(
        IReadOnlyList<CustomFieldInfo> definitions,
        IReadOnlyDictionary<string, string?> submitted,
        IReadOnlyDictionary<string, string> existing)
    {
        var errors = new List<string>();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (submitted.Count > MaxValues) return new(result, new[] { $"At most {MaxValues} custom values can be saved." });

        var active = definitions.Where(d => d.IsActive).ToDictionary(d => d.Key, StringComparer.Ordinal);

        foreach (var (key, raw) in submitted)
        {
            var value = raw?.Trim();
            if (!active.TryGetValue(key, out var def))
            {
                // A deactivated field's stored value may stay as it is, but cannot be changed or invented.
                if (!string.IsNullOrEmpty(value) && existing.TryGetValue(key, out var kept) && kept == value) result[key] = kept;
                else if (!string.IsNullOrEmpty(value)) errors.Add($"'{key}' is not an active custom field.");
                continue;
            }
            if (string.IsNullOrEmpty(value)) continue;

            var (ok, normalised, problem) = Normalise(def, value);
            if (ok) result[key] = normalised!;
            else errors.Add($"{def.Label}: {problem}");
        }

        // Stored values for fields the caller did not mention are kept (a partial submit never erases them).
        foreach (var (key, kept) in existing)
            if (!submitted.ContainsKey(key) && !result.ContainsKey(key)) result[key] = kept;

        foreach (var def in active.Values.Where(d => d.IsRequired && !result.ContainsKey(d.Key)))
            errors.Add($"{def.Label} is required.");

        return new(result, errors);
    }

    private static (bool Ok, string? Value, string? Problem) Normalise(CustomFieldInfo def, string value)
    {
        switch (def.Type)
        {
            case CustomFieldType.Text:
                return value.Length <= MaxTextLength ? (true, value, null) : (false, null, $"must be {MaxTextLength} characters or fewer.");
            case CustomFieldType.Number:
                return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && Math.Abs(n) < 1_000_000_000_000m
                    ? (true, n.ToString(CultureInfo.InvariantCulture), null)
                    : (false, null, "must be a number.");
            case CustomFieldType.Date:
                return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? (true, d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null)
                    : (false, null, "must be a date (yyyy-MM-dd).");
            case CustomFieldType.Boolean:
                return value.Equals("true", StringComparison.OrdinalIgnoreCase) ? (true, "true", null)
                    : value.Equals("false", StringComparison.OrdinalIgnoreCase) ? (true, "false", null)
                    : (false, null, "must be true or false.");
            case CustomFieldType.List:
                var match = def.Options.FirstOrDefault(o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase));
                return match is not null ? (true, match, null) : (false, null, $"must be one of: {string.Join(", ", def.Options)}.");
            default:
                return (false, null, "has an unsupported type.");
        }
    }
}
