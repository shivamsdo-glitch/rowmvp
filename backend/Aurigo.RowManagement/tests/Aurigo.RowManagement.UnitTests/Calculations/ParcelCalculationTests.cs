using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Domain.Enums;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Calculations;

/// <summary>ROW-072: ordered step gating with an Admin/PM override that needs a reason.</summary>
public class StepGateTests
{
    private static readonly IReadOnlyList<WorkflowStepInfo> Steps = new[]
    {
        new WorkflowStepInfo("setup", "Parcel set up", false),
        new WorkflowStepInfo("title", "Title & survey", false),
        new WorkflowStepInfo("appraisal", "Appraisal", false),
        new WorkflowStepInfo("offer", "Offer", true),
        new WorkflowStepInfo("closing", "Closing", true),
    };

    private static StepMoveResult Move(string from, string to, bool gateOpen = true, bool mayOverride = false, string? reason = null) =>
        StepGate.CanAdvance(Steps, from, to, gateOpen, mayOverride, reason);

    [Fact]
    public void ROW_072_The_next_step_in_order_is_always_allowed_without_an_override()
    {
        var r = Move("setup", "title");
        r.Outcome.Should().Be(StepMoveOutcome.Allowed);
        r.IsOverride.Should().BeFalse();
    }

    [Theory]
    [InlineData("setup", "appraisal")]   // skipping ahead
    [InlineData("appraisal", "title")]   // going back
    [InlineData("closing", "setup")]     // all the way back
    public void ROW_072_Anything_but_the_next_step_is_an_override_that_a_normal_role_cannot_make(string from, string to)
    {
        Move(from, to, mayOverride: false, reason: "because").Outcome.Should().Be(StepMoveOutcome.OverrideNotPermitted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ROW_072_An_override_needs_a_real_reason(string? reason)
    {
        Move("setup", "appraisal", mayOverride: true, reason: reason).Outcome.Should().Be(StepMoveOutcome.OverrideRequiresReason);
    }

    [Fact]
    public void ROW_072_An_override_with_a_reason_is_allowed_and_marked_as_an_override()
    {
        var r = Move("setup", "appraisal", mayOverride: true, reason: "Title was done under the earlier project");
        r.Outcome.Should().Be(StepMoveOutcome.AllowedWithOverride);
        r.Allowed.Should().BeTrue();
        r.IsOverride.Should().BeTrue();
    }

    [Fact]
    public void ROW_072_Going_back_is_an_override_too()
    {
        Move("appraisal", "title", mayOverride: true, reason: "Survey was wrong").IsOverride.Should().BeTrue();
    }

    [Fact]
    public void ROW_076_Offer_steps_are_held_while_the_acquisition_gate_is_closed_and_no_override_can_bypass_it()
    {
        var plain = Move("appraisal", "offer", gateOpen: false);
        plain.Outcome.Should().Be(StepMoveOutcome.BlockedByAcquisitionGate);

        var overridden = Move("setup", "closing", gateOpen: false, mayOverride: true, reason: "urgent");
        overridden.Outcome.Should().Be(StepMoveOutcome.BlockedByAcquisitionGate, "skipping across a gated step is still held");

        Move("appraisal", "offer", gateOpen: true).Outcome.Should().Be(StepMoveOutcome.Allowed);
    }

    [Fact]
    public void ROW_076_Steps_before_the_offer_are_never_held_and_going_back_is_never_held()
    {
        Move("setup", "title", gateOpen: false).Allowed.Should().BeTrue();
        Move("title", "appraisal", gateOpen: false).Allowed.Should().BeTrue();
        Move("closing", "appraisal", gateOpen: false, mayOverride: true, reason: "redo appraisal").Allowed.Should().BeTrue();
    }

    [Fact]
    public void Staying_put_and_unknown_steps_are_refused()
    {
        Move("title", "title").Outcome.Should().Be(StepMoveOutcome.AlreadyOnStep);
        Move("title", "nope").Outcome.Should().Be(StepMoveOutcome.UnknownStep);
        Move("nope", "title").Outcome.Should().Be(StepMoveOutcome.UnknownStep);
    }

    [Fact]
    public void ROW_072_Next_step_key_stops_at_the_end()
    {
        StepGate.NextStepKey(Steps, "setup").Should().Be("title");
        StepGate.NextStepKey(Steps, "closing").Should().BeNull();
        StepGate.NextStepKey(Steps, "nope").Should().BeNull();
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("ProjectManager", true)]
    [InlineData("Negotiator", false)]
    [InlineData("Finance", false)]
    public void ROW_072_Admin_and_project_manager_may_always_override(string role, bool expected)
    {
        StepGate.MayOverride(new[] { role }, Array.Empty<string>()).Should().Be(expected);
    }

    [Fact]
    public void ROW_072_An_administrator_can_grant_the_override_to_more_roles_and_any_one_role_is_enough()
    {
        StepGate.MayOverride(new[] { "Negotiator" }, new[] { "Negotiator" }).Should().BeTrue();
        StepGate.MayOverride(new[] { "Finance", "Negotiator" }, new[] { "Negotiator" }).Should().BeTrue();
        StepGate.MayOverride(new[] { "Finance" }, new[] { "Negotiator" }).Should().BeFalse();
        StepGate.MayOverride(Array.Empty<string>(), new[] { "Negotiator" }).Should().BeFalse();
    }
}

/// <summary>ROW-073, 074: statutory timelines as data, delay flags and next-action reminders.</summary>
public class StatutoryDeadlinesTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);
    private static readonly DeadlineRuleInfo Offer = new(Guid.NewGuid(), "Offer within 90 days of notice", "Notice to owner", "Offer delivered", 90);

    private static MilestoneInfo Done(string name, DateOnly on) => new(name, null, on);
    private static MilestoneInfo Open(string name) => new(name, null, null);

    [Fact]
    public void ROW_073_The_clock_does_not_start_until_the_trigger_milestone_is_completed()
    {
        var r = StatutoryDeadlines.Compute(Offer, new[] { Open("Notice to owner"), Open("Offer delivered") }, Today);
        r.Status.Should().Be(DeadlineStatus.NotStarted);
        r.DueOn.Should().BeNull();
        StatutoryDeadlines.Compute(Offer, Array.Empty<MilestoneInfo>(), Today).Status.Should().Be(DeadlineStatus.NotStarted);
    }

    [Fact]
    public void ROW_073_The_deadline_is_the_trigger_date_plus_the_rule_days()
    {
        var r = StatutoryDeadlines.Compute(Offer, new[] { Done("Notice to owner", new DateOnly(2026, 6, 1)) }, Today);
        r.DueOn.Should().Be(new DateOnly(2026, 8, 30));
        r.TriggerDate.Should().Be(new DateOnly(2026, 6, 1));
    }

    [Theory]
    [InlineData(-1, DeadlineStatus.Overdue)]   // yesterday was the deadline
    [InlineData(0, DeadlineStatus.DueSoon)]    // due today is not yet late
    [InlineData(7, DeadlineStatus.DueSoon)]    // exactly the warning window
    [InlineData(8, DeadlineStatus.Pending)]    // one day outside it
    public void ROW_073_Delay_flags_at_the_boundaries(int daysRemaining, DeadlineStatus expected)
    {
        var trigger = Today.AddDays(daysRemaining - Offer.Days);
        var r = StatutoryDeadlines.Compute(Offer, new[] { Done("Notice to owner", trigger) }, Today);
        r.Status.Should().Be(expected);
        r.DaysRemaining.Should().Be(daysRemaining);
    }

    [Fact]
    public void ROW_073_Completing_the_target_on_or_before_the_deadline_is_met_and_after_it_is_met_late()
    {
        var trigger = new DateOnly(2026, 1, 1);
        var due = trigger.AddDays(90);
        StatutoryDeadlines.Compute(Offer, new[] { Done("Notice to owner", trigger), Done("Offer delivered", due) }, Today).Status
            .Should().Be(DeadlineStatus.Met, "on the deadline day is on time");
        StatutoryDeadlines.Compute(Offer, new[] { Done("Notice to owner", trigger), Done("Offer delivered", due.AddDays(1)) }, Today).Status
            .Should().Be(DeadlineStatus.MetLate);
    }

    [Fact]
    public void ROW_073_Milestone_names_match_regardless_of_case()
    {
        var r = StatutoryDeadlines.Compute(Offer, new[] { Done("NOTICE TO OWNER", new DateOnly(2026, 6, 1)) }, Today);
        r.Status.Should().Be(DeadlineStatus.Pending);
    }

    [Fact]
    public void ROW_073_The_warning_window_is_configurable()
    {
        var milestones = new[] { Done("Notice to owner", Today.AddDays(20 - Offer.Days)) };
        StatutoryDeadlines.Compute(Offer, milestones, Today, dueSoonDays: 7).Status.Should().Be(DeadlineStatus.Pending);
        StatutoryDeadlines.Compute(Offer, milestones, Today, dueSoonDays: 30).Status.Should().Be(DeadlineStatus.DueSoon);
    }

    [Fact]
    public void ROW_074_Reminders_list_overdue_first_then_soonest_due_and_leave_the_rest_out()
    {
        var rule = (int days, string name) => new DeadlineRuleInfo(Guid.NewGuid(), name, "Start", "End", days);
        var ms = new[] { Done("Start", Today) };
        var results = new[]
        {
            StatutoryDeadlines.Compute(rule(30, "pending"), ms, Today),
            StatutoryDeadlines.Compute(rule(5, "soon"), ms, Today),
            StatutoryDeadlines.Compute(rule(-3, "late"), ms, Today),
            StatutoryDeadlines.Compute(rule(2, "sooner"), ms, Today),
            StatutoryDeadlines.Compute(rule(-10, "very late"), ms, Today),
            StatutoryDeadlines.Compute(rule(1, "met"), new[] { Done("Start", Today), Done("End", Today) }, Today),
            StatutoryDeadlines.Compute(rule(1, "not started"), Array.Empty<MilestoneInfo>(), Today),
        };

        StatutoryDeadlines.NeedingAttention(results).Select(r => r.Rule.Name)
            .Should().Equal("very late", "late", "sooner", "soon");
    }
}

/// <summary>ROW-006: roll-up counts and areas.</summary>
public class ParcelRollupTests
{
    private static readonly Guid Open = Guid.NewGuid(), Closed = Guid.NewGuid(), Unused = Guid.NewGuid();
    private static readonly Guid CorridorA = Guid.NewGuid();

    [Fact]
    public void ROW_006_Counts_and_areas_are_summed_per_status_and_group_and_empty_buckets_are_kept()
    {
        var result = ParcelRollup.Summarize(new[]
        {
            new RollupParcel(Open, CorridorA, 1000m, ParcelState.Active),
            new RollupParcel(Open, null, 500.5m, ParcelState.Active),
            new RollupParcel(Closed, CorridorA, 250m, ParcelState.Active),
        }, new[] { Open, Closed, Unused }, new[] { CorridorA });

        result.TotalCount.Should().Be(3);
        result.TotalAreaSqFt.Should().Be(1750.5m);
        result.ByStatus.Select(b => (b.Key, b.Count, b.AreaSqFt)).Should().Equal((Open, 2, 1500.5m), (Closed, 1, 250m), (Unused, 0, 0m));
        result.ByGroup.Select(b => (b.Key, b.Count)).Should().Equal((CorridorA, 2), (null, 1));
    }

    [Fact]
    public void ROW_007_Split_and_combined_parcels_do_not_count()
    {
        var result = ParcelRollup.Summarize(new[]
        {
            new RollupParcel(Open, null, 1000m, ParcelState.Split),
            new RollupParcel(Open, null, 300m, ParcelState.Active),
            new RollupParcel(Open, null, 200m, ParcelState.Active),
            new RollupParcel(Open, null, 500m, ParcelState.Combined),
        }, new[] { Open }, Array.Empty<Guid>());

        result.TotalCount.Should().Be(2);
        result.TotalAreaSqFt.Should().Be(500m);
    }

    [Fact]
    public void ROW_006_A_parcel_with_a_status_no_longer_in_the_list_is_still_counted()
    {
        var removed = Guid.NewGuid();
        var result = ParcelRollup.Summarize(new[] { new RollupParcel(removed, null, 10m, ParcelState.Active) }, new[] { Open }, Array.Empty<Guid>());

        result.ByStatus.Should().Contain(b => b.Key == removed && b.Count == 1);
    }

    [Fact]
    public void ROW_006_Nothing_gives_zeros_and_acres_convert()
    {
        var result = ParcelRollup.Summarize(Array.Empty<RollupParcel>(), new[] { Open }, Array.Empty<Guid>());
        result.TotalCount.Should().Be(0);
        result.TotalAreaSqFt.Should().Be(0m);
        result.ByGroup.Should().BeEmpty();

        ParcelRollup.ToAcres(43_560m).Should().Be(1m);
        ParcelRollup.ToAcres(21_780m).Should().Be(0.5m);
        ParcelRollup.ToAcres(0m).Should().Be(0m);
    }
}

/// <summary>ROW-007: split with remnant, and parcel numbering.</summary>
public class ParcelSplitTests
{
    [Fact]
    public void ROW_007_Parts_that_use_less_than_the_whole_leave_a_remnant()
    {
        var plan = ParcelSplit.Plan("012", 10_000m, new[] { 3_000m, 2_500m });

        plan.RemnantAreaSqFt.Should().Be(4_500m);
        plan.Numbers.Should().Equal("012-A", "012-B");
        plan.RemnantNumber.Should().Be("012-R");
    }

    [Fact]
    public void ROW_007_Parts_that_use_exactly_the_whole_parcel_leave_no_remnant()
    {
        var plan = ParcelSplit.Plan("012", 10_000m, new[] { 6_000m, 4_000m });

        plan.RemnantAreaSqFt.Should().Be(0m);
        plan.RemnantNumber.Should().BeNull();
    }

    [Fact]
    public void ROW_007_Validation_accepts_the_whole_area_and_rejects_one_square_foot_more()
    {
        ParcelSplit.Validate(1000m, new[] { 600m, 400m }).Should().BeEmpty();
        ParcelSplit.Validate(1000m, new[] { 600m, 400.01m }).Should().ContainSingle().Which.Should().Contain("more than the parcel");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ROW_007_Every_part_needs_an_area_above_zero(int area)
    {
        ParcelSplit.Validate(1000m, new[] { 500m, area }).Should().ContainSingle().Which.Should().Contain("greater than zero");
    }

    [Fact]
    public void ROW_007_At_least_one_and_at_most_twenty_five_parts()
    {
        ParcelSplit.Validate(1000m, Array.Empty<decimal>()).Should().ContainSingle().Which.Should().Contain("At least one");
        ParcelSplit.Validate(1_000_000m, Enumerable.Repeat(1m, ParcelSplit.MaxParts).ToList()).Should().BeEmpty();
        ParcelSplit.Validate(1_000_000m, Enumerable.Repeat(1m, ParcelSplit.MaxParts + 1).ToList()).Should().ContainSingle().Which.Should().Contain("at most");
    }

    [Fact]
    public void ROW_007_Part_letters_never_collide_with_the_remnant_letter()
    {
        var plan = ParcelSplit.Plan("9", 1_000_000m, Enumerable.Repeat(1m, ParcelSplit.MaxParts).ToList());

        plan.Numbers.Should().OnlyHaveUniqueItems().And.NotContain("9-R");
        plan.Numbers[0].Should().Be("9-A");
        plan.Numbers[^1].Should().Be("9-Z");
        plan.RemnantNumber.Should().Be("9-R");
    }

    [Theory]
    [InlineData(new string[0], "001")]
    [InlineData(new[] { "001", "002" }, "003")]
    [InlineData(new[] { "9" }, "010")]
    [InlineData(new[] { "1", "A-12", "12-A", "007" }, "008")]
    [InlineData(new[] { "999" }, "1000")]
    public void ROW_005_The_next_parcel_number_follows_the_highest_plain_number(string[] existing, string expected) =>
        ParcelSplit.NextNumber(existing).Should().Be(expected);
}
