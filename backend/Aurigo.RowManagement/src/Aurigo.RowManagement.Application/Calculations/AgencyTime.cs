using System.Collections.Concurrent;

namespace Aurigo.RowManagement.Application.Calculations;

public sealed record TimeZoneOption(string Id, string Label);

/// <summary>
/// Which calendar day it is where the work is. Deadlines, "due today" and "completed on" are calendar days, and the same
/// instant is a different day in different places, so every day count is made in the time zone of the project concerned
/// (falling back to the agency's). Pure: the instant is passed in.
/// </summary>
public static class AgencyTime
{
    public const string Utc = "UTC";

    /// <summary>The zones an agency can choose: UTC and the US zones (IANA ids).</summary>
    public static readonly IReadOnlyList<TimeZoneOption> Supported = new TimeZoneOption[]
    {
        new(Utc, "UTC (Coordinated Universal Time)"),
        new("America/New_York", "Eastern Time (New York)"),
        new("America/Detroit", "Eastern Time (Michigan)"),
        new("America/Indiana/Indianapolis", "Eastern Time (Indiana)"),
        new("America/Kentucky/Louisville", "Eastern Time (Kentucky)"),
        new("America/Chicago", "Central Time (Chicago)"),
        new("America/Denver", "Mountain Time (Denver)"),
        new("America/Boise", "Mountain Time (Idaho)"),
        new("America/Phoenix", "Mountain Time, no daylight saving (Arizona)"),
        new("America/Los_Angeles", "Pacific Time (Los Angeles)"),
        new("America/Anchorage", "Alaska Time"),
        new("America/Adak", "Hawaii-Aleutian Time (Aleutian Islands)"),
        new("Pacific/Honolulu", "Hawaii Time, no daylight saving"),
        new("America/Puerto_Rico", "Atlantic Time (Puerto Rico, US Virgin Islands)"),
        new("Pacific/Guam", "Chamorro Time (Guam, Northern Mariana Islands)"),
        new("Pacific/Pago_Pago", "Samoa Time (American Samoa)"),
    };

    private static readonly ConcurrentDictionary<string, TimeZoneInfo> Zones = new(StringComparer.Ordinal);

    public static bool IsSupported(string? id) => id is not null && Supported.Any(z => string.Equals(z.Id, id, StringComparison.Ordinal));

    /// <summary>The zone that applies: the project's own if it has one, else the agency's, else UTC.</summary>
    public static string Resolve(string? projectZone, string? agencyZone) =>
        !string.IsNullOrWhiteSpace(projectZone) ? projectZone : !string.IsNullOrWhiteSpace(agencyZone) ? agencyZone : Utc;

    /// <summary>The calendar day it is in <paramref name="timeZoneId"/> at the instant <paramref name="utcNow"/>.</summary>
    public static DateOnly TodayIn(string timeZoneId, DateTimeOffset utcNow)
    {
        if (!IsSupported(timeZoneId)) throw new ArgumentException($"'{timeZoneId}' is not a supported time zone.", nameof(timeZoneId));
        var zone = Zones.GetOrAdd(timeZoneId, TimeZoneInfo.FindSystemTimeZoneById);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);
    }

    /// <summary>
    /// The latest calendar day it can be anywhere in the world right now (UTC+14). Used to pick candidates generously before
    /// each one is judged in its own zone.
    /// </summary>
    public static DateOnly LatestTodayAnywhere(DateTimeOffset utcNow) => DateOnly.FromDateTime(utcNow.UtcDateTime.AddHours(14));
}
