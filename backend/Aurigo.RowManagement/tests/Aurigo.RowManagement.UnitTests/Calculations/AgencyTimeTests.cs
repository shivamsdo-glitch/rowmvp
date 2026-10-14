using Aurigo.RowManagement.Application.Calculations;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Calculations;

/// <summary>Which calendar day it is where the work is (ROW-073): deadlines and "today" are counted per time zone.</summary>
public class AgencyTimeTests
{
    private static DateTimeOffset At(int y, int mo, int d, int h, int mi = 0, int s = 0) => new(y, mo, d, h, mi, s, TimeSpan.Zero);

    [Fact]
    public void ROW_073_Every_listed_zone_exists_on_this_machine_and_is_listed_once()
    {
        AgencyTime.Supported.Select(z => z.Id).Should().OnlyHaveUniqueItems();
        AgencyTime.Supported.Should().OnlyContain(z => z.Label.Length > 0);
        foreach (var zone in AgencyTime.Supported)
        {
            var act = () => AgencyTime.TodayIn(zone.Id, At(2026, 3, 15, 12));
            act.Should().NotThrow($"{zone.Id} must resolve (the host needs its time zone database)");
        }
    }

    [Fact]
    public void ROW_073_The_same_instant_is_a_different_day_in_different_places()
    {
        var instant = At(2026, 3, 16, 2, 30); // 10:30 pm on the 15th in New York (EDT), still the 15th in Los Angeles

        AgencyTime.TodayIn("UTC", instant).Should().Be(new DateOnly(2026, 3, 16));
        AgencyTime.TodayIn("America/New_York", instant).Should().Be(new DateOnly(2026, 3, 15));
        AgencyTime.TodayIn("America/Chicago", instant).Should().Be(new DateOnly(2026, 3, 15));
        AgencyTime.TodayIn("America/Los_Angeles", instant).Should().Be(new DateOnly(2026, 3, 15));
        AgencyTime.TodayIn("Pacific/Honolulu", instant).Should().Be(new DateOnly(2026, 3, 15));
        AgencyTime.TodayIn("Pacific/Pago_Pago", instant).Should().Be(new DateOnly(2026, 3, 15));
        AgencyTime.TodayIn("Pacific/Guam", instant).Should().Be(new DateOnly(2026, 3, 16), "Guam is already well into the 16th");
    }

    [Theory]
    [InlineData("America/New_York", 2026, 3, 16, 3, 59, 59, 15)] // one second before local midnight (EDT, UTC-4)
    [InlineData("America/New_York", 2026, 3, 16, 4, 0, 0, 16)]   // local midnight exactly
    [InlineData("America/New_York", 2026, 1, 16, 4, 59, 59, 15)] // winter (EST, UTC-5): the same wall clock is an hour later in UTC
    [InlineData("America/New_York", 2026, 1, 16, 5, 0, 0, 16)]
    [InlineData("America/Phoenix", 2026, 7, 16, 6, 59, 59, 15)]  // no daylight saving: always UTC-7
    [InlineData("America/Phoenix", 2026, 7, 16, 7, 0, 0, 16)]
    [InlineData("America/Phoenix", 2026, 1, 16, 6, 59, 59, 15)]
    [InlineData("America/Phoenix", 2026, 1, 16, 7, 0, 0, 16)]
    public void ROW_073_A_day_changes_at_the_zones_own_local_midnight_with_and_without_daylight_saving(
        string zone, int y, int mo, int d, int h, int mi, int s, int expectedDay)
    {
        AgencyTime.TodayIn(zone, At(y, mo, d, h, mi, s)).Should().Be(new DateOnly(y, mo, expectedDay));
    }

    [Fact]
    public void ROW_073_Daylight_saving_changes_move_the_boundary_by_an_hour_on_the_day_the_clocks_change()
    {
        // US clocks went forward on 8 March 2026 at 2 am local (07:00 UTC) and back on 1 November 2026 at 2 am (06:00 UTC).
        AgencyTime.TodayIn("America/New_York", At(2026, 3, 8, 4, 59)).Should().Be(new DateOnly(2026, 3, 7), "still EST (UTC-5): 11:59 pm on the 7th");
        AgencyTime.TodayIn("America/New_York", At(2026, 3, 8, 5, 0)).Should().Be(new DateOnly(2026, 3, 8));
        AgencyTime.TodayIn("America/New_York", At(2026, 11, 1, 3, 59)).Should().Be(new DateOnly(2026, 10, 31), "still EDT (UTC-4): 11:59 pm on the 31st");
        AgencyTime.TodayIn("America/New_York", At(2026, 11, 1, 4, 0)).Should().Be(new DateOnly(2026, 11, 1));
    }

    [Fact]
    public void ROW_073_A_project_zone_wins_over_the_agency_zone_which_wins_over_UTC()
    {
        AgencyTime.Resolve("America/Denver", "America/New_York").Should().Be("America/Denver");
        AgencyTime.Resolve(null, "America/New_York").Should().Be("America/New_York");
        AgencyTime.Resolve("", "America/New_York").Should().Be("America/New_York");
        AgencyTime.Resolve(null, null).Should().Be("UTC");
        AgencyTime.Resolve(" ", " ").Should().Be("UTC");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("america/new_york")] // ids are exact
    [InlineData("Europe/London")]    // real, but not offered
    [InlineData("EST")]
    public void ROW_073_Only_the_listed_zones_are_supported(string? id)
    {
        AgencyTime.IsSupported(id).Should().BeFalse();
        if (id is not null) ((Action)(() => AgencyTime.TodayIn(id, At(2026, 3, 15, 12)))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ROW_073_The_generous_cut_off_is_never_earlier_than_the_day_in_any_listed_zone()
    {
        // Reminders pick candidates with this day before judging each parcel on its own calendar, so it must never be too early.
        for (var minute = 0; minute < 3 * 24 * 60; minute += 30)
        {
            var instant = At(2026, 3, 7, 0).AddMinutes(minute); // spans the March clock change
            var latest = AgencyTime.LatestTodayAnywhere(instant);
            foreach (var zone in AgencyTime.Supported)
                latest.Should().BeOnOrAfter(AgencyTime.TodayIn(zone.Id, instant), $"{zone.Id} at {instant:u}");
        }
    }
}
