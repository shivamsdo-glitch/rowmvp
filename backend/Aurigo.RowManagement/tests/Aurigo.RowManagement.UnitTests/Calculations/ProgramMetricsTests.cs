using Aurigo.RowManagement.Application.Calculations;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Calculations;

/// <summary>ADR 0024: the ROW Director's numbers. How long parcels sit in each step, which team that is, and how healthy a project is.</summary>
public class StageTimesTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Order = { "setup", "title_survey", "appraisal", "offer", "negotiation", "agreement", "closing" };
    private static DateTime Ago(double days) => Now.AddDays(-days);
    private static StepMove Move(string? from, string to, double daysAgo) => new(from, to, Ago(daysAgo));
    private static ParcelStepHistory H(string current, double createdDaysAgo, params StepMove[] moves) => new(Guid.NewGuid(), current, Ago(createdDaysAgo), moves);

    [Fact]
    public void A_parcel_that_never_moved_has_been_on_its_first_step_since_it_was_set_up()
    {
        var stays = StageTimes.Stays(H("setup", 12));
        var stay = stays.Should().ContainSingle().Subject;
        (stay.StepKey, stay.IsOpen).Should().Be(("setup", true));
        stay.DaysAt(Now).Should().BeApproximately(12, 0.001);
    }

    [Fact]
    public void Each_move_closes_one_stay_and_opens_the_next_with_no_gap_and_no_overlap()
    {
        var stays = StageTimes.Stays(H("appraisal", 30, Move("setup", "title_survey", 20), Move("title_survey", "appraisal", 5)));
        stays.Select(s => s.StepKey).Should().Equal("setup", "title_survey", "appraisal");
        stays.Select(s => Math.Round(s.DaysAt(Now))).Should().Equal(10, 15, 5);
        stays.Select(s => s.IsOpen).Should().Equal(false, false, true);
        stays[0].Left.Should().Be(stays[1].Entered);
        stays[1].Left.Should().Be(stays[2].Entered);
    }

    [Fact]
    public void Moves_are_read_in_time_order_whatever_order_they_arrive_in()
    {
        var stays = StageTimes.Stays(H("appraisal", 30, Move("title_survey", "appraisal", 5), Move("setup", "title_survey", 20)));
        stays.Select(s => s.StepKey).Should().Equal("setup", "title_survey", "appraisal");
    }

    [Fact]
    public void A_step_visited_twice_after_a_reversal_is_two_stays()
    {
        var stays = StageTimes.Stays(H("appraisal", 40, Move("setup", "appraisal", 30), Move("appraisal", "title_survey", 20), Move("title_survey", "appraisal", 10)));
        stays.Count(s => s.StepKey == "appraisal").Should().Be(2);
    }

    [Fact]
    public void Summarize_counts_where_parcels_are_now_and_how_long_finished_and_current_stays_lasted()
    {
        var a = H("appraisal", 30, Move("setup", "title_survey", 25), Move("title_survey", "appraisal", 15)); // title 10d (done), appraisal 15d (open)
        var b = H("appraisal", 20, Move("setup", "title_survey", 18), Move("title_survey", "appraisal", 4)); // title 14d (done), appraisal 4d (open)
        var c = H("setup", 3);                                                                              // setup 3d (open)
        var rows = StageTimes.Summarize(new[] { a, b, c }, Order, Now).ToDictionary(r => r.StepKey);

        rows.Keys.Should().Contain(Order, "every step is listed, even one nobody is on");
        rows["title_survey"].CompletedStays.Should().Be(2);
        rows["title_survey"].AvgDaysCompleted.Should().BeApproximately(12, 0.001);
        rows["title_survey"].ParcelsNow.Should().Be(0);
        rows["appraisal"].ParcelsNow.Should().Be(2);
        rows["appraisal"].AvgDaysOpen.Should().BeApproximately(9.5, 0.001);
        rows["appraisal"].LongestOpenDays.Should().BeApproximately(15, 0.001);
        rows["setup"].ParcelsNow.Should().Be(1);
        rows["closing"].AvgDaysCompleted.Should().BeNull();
        rows["closing"].AvgDaysOpen.Should().BeNull();
    }

    [Fact]
    public void A_stay_is_stuck_only_when_it_is_open_and_longer_than_the_limit()
    {
        var rows = StageTimes.Summarize(new[] { H("offer", 31), H("offer", 30), H("offer", 5) }, Order, Now, stuckDays: 30).ToDictionary(r => r.StepKey);
        rows["offer"].Stuck.Should().Be(1, "31 days is over 30; exactly 30 is not");
        var finished = StageTimes.Summarize(new[] { H("appraisal", 200, Move("setup", "appraisal", 5)) }, Order, Now).ToDictionary(r => r.StepKey);
        finished["setup"].Stuck.Should().Be(0, "a long stay that ended is history, not stuck");
    }

    [Theory]
    [InlineData("setup", "Project team")]
    [InlineData("title_survey", "Title & Survey")]
    [InlineData("appraisal", "Appraisal")]
    [InlineData("offer", "Negotiation")]
    [InlineData("negotiation", "Negotiation")]
    [InlineData("agreement", "Negotiation")]
    [InlineData("closing", "Closing")]
    [InlineData("early_approval", "Project team")]
    [InlineData("donation_review", "Negotiation")]
    [InlineData("conveyance_terms", "Negotiation")]
    [InlineData("something_new", "Other")]
    public void Every_step_belongs_to_a_team(string step, string team) => StageTimes.TeamOf(step).Should().Be(team);

    [Fact]
    public void Teams_are_ranked_by_how_long_a_parcel_stays_with_them_longest_first()
    {
        var slowAppraisal = H("negotiation", 100, Move("setup", "appraisal", 90), Move("appraisal", "negotiation", 10)); // appraisal 80d, setup 10d
        var quickTitle = H("appraisal", 30, Move("setup", "title_survey", 28), Move("title_survey", "appraisal", 25));    // setup 2d, title 3d, appraisal 25d open
        var teams = StageTimes.ByTeam(new[] { slowAppraisal, quickTitle }, Now);

        teams[0].Team.Should().Be("Appraisal");
        teams[0].AvgDaysPerStay.Should().BeApproximately((80 + 25) / 2.0, 0.001);
        teams.Select(t => t.AvgDaysPerStay).Should().BeInDescendingOrder();
        teams.Single(t => t.Team == "Negotiation").ParcelsNow.Should().Be(1);
        teams.Single(t => t.Team == "Appraisal").ParcelsNow.Should().Be(1);
        teams.Single(t => t.Team == "Title & Survey").TotalDays.Should().BeApproximately(3, 0.001);
    }

    [Fact]
    public void Several_steps_of_one_team_pool_into_that_team()
    {
        var p = H("agreement", 60, Move("setup", "offer", 50), Move("offer", "negotiation", 30), Move("negotiation", "agreement", 10));
        var team = StageTimes.ByTeam(new[] { p }, Now).Single(t => t.Team == "Negotiation");
        team.Stays.Should().Be(3);
        team.TotalDays.Should().BeApproximately(20 + 20 + 10, 0.001);
        team.ParcelsNow.Should().Be(1);
    }

    [Fact]
    public void No_parcels_gives_empty_teams_and_steps_with_nothing_in_them()
    {
        StageTimes.ByTeam(Array.Empty<ParcelStepHistory>(), Now).Should().BeEmpty();
        StageTimes.Summarize(Array.Empty<ParcelStepHistory>(), Order, Now).Should().HaveCount(Order.Length).And.OnlyContain(r => r.ParcelsNow == 0 && r.AvgDaysOpen == null);
    }
}

public class ProjectHealthTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static ProjectHealthInput Input(int active = 10, int closed = 2, int overdue = 0, int stuck = 0, DateOnly? clear = null, decimal? estimate = null, decimal known = 0) =>
        new(active, closed, overdue, stuck, clear, Today, estimate, known);
    private static ProjectHealthResult Run(ProjectHealthInput i) => ProjectHealth.Evaluate(i);

    [Fact]
    public void A_project_with_no_parcels_has_no_health_yet() => Run(Input(active: 0, closed: 0)).Level.Should().Be(ProjectHealthLevel.None);

    [Fact]
    public void Nothing_wrong_is_green_with_no_reasons()
    {
        var r = Run(Input());
        r.Level.Should().Be(ProjectHealthLevel.Green);
        r.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void One_overdue_deadline_is_amber_and_says_so()
    {
        var r = Run(Input(overdue: 1));
        r.Level.Should().Be(ProjectHealthLevel.Amber);
        r.Reasons.Should().ContainSingle().Which.Should().Contain("1 parcel has an overdue deadline");
    }

    [Fact]
    public void Two_overdue_parcels_are_still_only_amber_because_red_needs_at_least_three() =>
        Run(Input(active: 4, overdue: 2)).Level.Should().Be(ProjectHealthLevel.Amber);

    [Fact]
    public void Three_overdue_parcels_is_red_on_a_small_project_but_amber_on_a_big_one()
    {
        Run(Input(active: 10, overdue: 3)).Level.Should().Be(ProjectHealthLevel.Red);
        Run(Input(active: 40, overdue: 3)).Level.Should().Be(ProjectHealthLevel.Amber);
    }

    [Fact]
    public void A_passed_clear_date_with_parcels_still_open_is_red()
    {
        var r = Run(Input(clear: Today.AddDays(-1), closed: 9));
        r.Level.Should().Be(ProjectHealthLevel.Red);
        r.Reasons.Single().Should().Contain("has passed with 1 parcel not closed");
    }

    [Fact]
    public void A_passed_clear_date_with_everything_closed_is_fine() =>
        Run(Input(clear: Today.AddDays(-30), closed: 10)).Level.Should().Be(ProjectHealthLevel.Green);

    [Fact]
    public void A_clear_date_inside_the_warning_window_with_too_little_closed_is_amber_and_a_comfortable_one_is_green()
    {
        Run(Input(clear: Today.AddDays(ProjectHealth.ClearDateWarningDays), closed: 7)).Level.Should().Be(ProjectHealthLevel.Amber);
        Run(Input(clear: Today.AddDays(ProjectHealth.ClearDateWarningDays), closed: 8)).Level.Should().Be(ProjectHealthLevel.Green, "80% closed is past the 75% bar");
        Run(Input(clear: Today.AddDays(ProjectHealth.ClearDateWarningDays + 1), closed: 0)).Level.Should().Be(ProjectHealthLevel.Green, "too far away to worry");
        Run(Input(clear: Today, closed: 0)).Level.Should().Be(ProjectHealthLevel.Amber, "today is not yet passed");
    }

    [Fact]
    public void Known_cost_over_the_estimate_is_red_and_exactly_on_it_is_not()
    {
        Run(Input(estimate: 1_000_000m, known: 1_000_001m)).Level.Should().Be(ProjectHealthLevel.Red);
        Run(Input(estimate: 1_000_000m, known: 1_000_000m)).Level.Should().Be(ProjectHealthLevel.Green);
        Run(Input(estimate: null, known: 5_000_000m)).Level.Should().Be(ProjectHealthLevel.Green, "no estimate, nothing to compare");
        Run(Input(estimate: 0m, known: 5_000_000m)).Level.Should().Be(ProjectHealthLevel.Green, "a zero estimate is no estimate");
    }

    [Fact]
    public void Stuck_parcels_make_a_project_amber()
    {
        var r = Run(Input(stuck: 2));
        r.Level.Should().Be(ProjectHealthLevel.Amber);
        r.Reasons.Single().Should().Contain("2 parcels have been on one step");
    }

    [Fact]
    public void Red_wins_and_every_reason_is_listed_red_first()
    {
        var r = Run(Input(overdue: 1, stuck: 1, estimate: 100m, known: 200m));
        r.Level.Should().Be(ProjectHealthLevel.Red);
        r.Reasons.Should().HaveCount(3);
        r.Reasons[0].Should().Contain("over the estimate");
    }
}
