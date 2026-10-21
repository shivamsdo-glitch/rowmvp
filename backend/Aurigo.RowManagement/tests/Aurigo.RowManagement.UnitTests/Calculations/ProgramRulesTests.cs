using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Domain.Entities;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Calculations;

/// <summary>ADR 0025: the thresholds and the step-to-team mapping are the agency's to change; the defaults stay what ADR 0024 set.</summary>
public class ProgramRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static ParcelStepHistory H(string current, double createdDaysAgo, params StepMove[] moves) => new(Guid.NewGuid(), current, Now.AddDays(-createdDaysAgo), moves);
    private static ProjectHealthInput Input(int active = 10, int closed = 2, int overdue = 0, int stuck = 0, DateOnly? clear = null, decimal? estimate = null, decimal known = 0) =>
        new(active, closed, overdue, stuck, clear, Today, estimate, known);

    [Fact]
    public void The_defaults_are_what_the_health_rules_and_the_stored_settings_start_with()
    {
        var d = ProgramRules.Default;
        (d.StuckDays, d.ClearDateWarningDays, d.ClearDateWarningPercentClosed, d.RedOverdueMinimumParcels, d.RedOverduePercent).Should().Be((30, 90, 75, 3, 25));
        (d.StuckDays, d.ClearDateWarningDays, d.ClearDateWarningPercentClosed, d.RedOverdueMinimumParcels, d.RedOverduePercent)
            .Should().Be((StageTimes.DefaultStuckDays, ProjectHealth.ClearDateWarningDays, ProjectHealth.ClearDateWarningPercentClosed, ProjectHealth.RedOverdueMinimumParcels, ProjectHealth.RedOverduePercent));
        var row = new ProgramSettings();
        (row.StuckDays, row.ClearDateWarningDays, row.ClearDateWarningPercentClosed, row.RedOverdueMinimumParcels, row.RedOverduePercent).Should().Be((30, 90, 75, 3, 25));
    }

    [Fact]
    public void A_step_follows_the_default_team_until_the_agency_chooses_another()
    {
        StageTimes.TeamOf("appraisal").Should().Be("Appraisal");
        StageTimes.TeamOf("appraisal", null).Should().Be("Appraisal");
        StageTimes.TeamOf("appraisal", new Dictionary<string, string>()).Should().Be("Appraisal");
        var chosen = new Dictionary<string, string> { ["appraisal"] = "Valuation", ["closing"] = "Title & Survey" };
        StageTimes.TeamOf("appraisal", chosen).Should().Be("Valuation");
        StageTimes.TeamOf("closing", chosen).Should().Be("Title & Survey");
        StageTimes.TeamOf("offer", chosen).Should().Be("Negotiation", "a step the agency did not choose keeps its default");
        StageTimes.DefaultTeamOf("appraisal").Should().Be("Appraisal", "the default is still available to compare with");
    }

    [Fact]
    public void Stage_rows_and_team_totals_use_the_agencys_mapping()
    {
        var p = H("negotiation", 60, new StepMove("setup", "appraisal", Now.AddDays(-50)), new StepMove("appraisal", "negotiation", Now.AddDays(-20)));
        var chosen = new Dictionary<string, string> { ["appraisal"] = "Valuation" };

        StageTimes.Summarize(new[] { p }, new[] { "setup", "appraisal", "negotiation" }, Now, teams: chosen).Single(r => r.StepKey == "appraisal").Team.Should().Be("Valuation");
        var teams = StageTimes.ByTeam(new[] { p }, Now, teams: chosen);
        teams.Select(t => t.Team).Should().Contain("Valuation").And.NotContain("Appraisal");
        teams.Single(t => t.Team == "Valuation").TotalDays.Should().BeApproximately(30, 0.001);
    }

    [Fact]
    public void Two_steps_mapped_to_one_team_pool_their_time()
    {
        var p = H("closing", 100, new StepMove("setup", "title_survey", Now.AddDays(-90)), new StepMove("title_survey", "closing", Now.AddDays(-60)));
        var chosen = new Dictionary<string, string> { ["closing"] = "Title & Survey" };
        var team = StageTimes.ByTeam(new[] { p }, Now, teams: chosen).Single(t => t.Team == "Title & Survey");
        team.Stays.Should().Be(2);
        team.TotalDays.Should().BeApproximately(30 + 60, 0.001);
    }

    [Fact]
    public void The_stuck_limit_the_agency_sets_decides_which_stays_are_stuck()
    {
        var parcels = new[] { H("offer", 20), H("offer", 8) };
        StageTimes.Summarize(parcels, new[] { "offer" }, Now, stuckDays: 30).Single(r => r.StepKey == "offer").Stuck.Should().Be(0);
        StageTimes.Summarize(parcels, new[] { "offer" }, Now, stuckDays: 10).Single(r => r.StepKey == "offer").Stuck.Should().Be(1);
        StageTimes.ByTeam(parcels, Now, stuckDays: 7).Single(t => t.Team == "Negotiation").Stuck.Should().Be(2);
    }

    [Fact]
    public void A_wider_clear_date_warning_turns_a_comfortable_project_amber()
    {
        var clear = Today.AddDays(120);
        ProjectHealth.Evaluate(Input(clear: clear, closed: 0)).Level.Should().Be(ProjectHealthLevel.Green);
        var wide = ProgramRules.Default with { ClearDateWarningDays = 180 };
        ProjectHealth.Evaluate(Input(clear: clear, closed: 0), wide).Level.Should().Be(ProjectHealthLevel.Amber);
    }

    [Fact]
    public void A_higher_closed_bar_turns_a_well_progressed_project_amber_and_a_lower_one_green()
    {
        var clear = Today.AddDays(30);
        ProjectHealth.Evaluate(Input(clear: clear, closed: 8)).Level.Should().Be(ProjectHealthLevel.Green);
        ProjectHealth.Evaluate(Input(clear: clear, closed: 8), ProgramRules.Default with { ClearDateWarningPercentClosed = 90 }).Level.Should().Be(ProjectHealthLevel.Amber);
        ProjectHealth.Evaluate(Input(clear: clear, closed: 5), ProgramRules.Default with { ClearDateWarningPercentClosed = 50 }).Level.Should().Be(ProjectHealthLevel.Green);
    }

    [Fact]
    public void The_red_overdue_rule_follows_the_minimum_count_and_the_share_the_agency_sets()
    {
        ProjectHealth.Evaluate(Input(active: 6, overdue: 2)).Level.Should().Be(ProjectHealthLevel.Amber);
        ProjectHealth.Evaluate(Input(active: 6, overdue: 2), ProgramRules.Default with { RedOverdueMinimumParcels = 2 }).Level.Should().Be(ProjectHealthLevel.Red);
        ProjectHealth.Evaluate(Input(active: 40, overdue: 3)).Level.Should().Be(ProjectHealthLevel.Amber);
        ProjectHealth.Evaluate(Input(active: 40, overdue: 3), ProgramRules.Default with { RedOverduePercent = 5 }).Level.Should().Be(ProjectHealthLevel.Red);
        ProjectHealth.Evaluate(Input(active: 10, overdue: 5), ProgramRules.Default with { RedOverduePercent = 80 }).Level.Should().Be(ProjectHealthLevel.Amber, "5 of 10 is 50%, under the 80% bar");
    }

    [Fact]
    public void The_stuck_reason_names_the_limit_in_force()
    {
        var reason = ProjectHealth.Evaluate(Input(stuck: 1), ProgramRules.Default with { StuckDays = 45 }).Reasons.Single();
        reason.Should().Contain("over 45 days").And.NotContain("30");
    }
}
