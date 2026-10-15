using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Calculations;

/// <summary>ROW-077 rights of entry, ROW-239 plan-review gate, ROW-080/240 approval route, ROW-241 acquisition plan.</summary>
public class AcquisitionRulesTests
{
    private static readonly DateOnly Today = new(2026, 3, 15);

    // ── ROW-077 ──

    [Fact]
    public void ROW_077_A_granted_right_needs_the_date_permission_was_given()
    {
        RightOfEntryRules.Problems(RightOfEntryStatus.Granted, null, null, null, Today).Should().Contain("Enter the date permission was given.");
        RightOfEntryRules.Problems(RightOfEntryStatus.Granted, Today.AddDays(-5), Today.AddDays(-2), Today.AddDays(30), Today).Should().BeEmpty();
    }

    [Fact]
    public void ROW_077_A_requested_or_denied_right_has_no_granted_date()
    {
        RightOfEntryRules.Problems(RightOfEntryStatus.Requested, null, Today, null, Today).Should().Contain(p => p.Contains("Only a granted"));
        RightOfEntryRules.Problems(RightOfEntryStatus.Denied, null, Today, null, Today).Should().Contain(p => p.Contains("Only a granted"));
        RightOfEntryRules.Problems(RightOfEntryStatus.Requested, Today, null, null, Today).Should().BeEmpty();
    }

    [Fact]
    public void ROW_077_Dates_cannot_be_in_the_future_or_run_backwards()
    {
        RightOfEntryRules.Problems(RightOfEntryStatus.Requested, Today.AddDays(1), null, null, Today).Should().Contain(p => p.Contains("request date"));
        RightOfEntryRules.Problems(RightOfEntryStatus.Granted, null, Today.AddDays(1), null, Today).Should().Contain(p => p.Contains("future"));
        RightOfEntryRules.Problems(RightOfEntryStatus.Granted, null, Today.AddDays(-2), Today.AddDays(-3), Today).Should().Contain("Permission cannot end before it was given.");
        RightOfEntryRules.Problems(RightOfEntryStatus.Granted, Today.AddDays(-1), Today.AddDays(-4), null, Today).Should().Contain("Permission cannot be given before it was requested.");
    }

    [Fact]
    public void ROW_077_The_same_problem_is_reported_once()
    {
        RightOfEntryRules.Problems(RightOfEntryStatus.Granted, null, null, Today, Today).Should().ContainSingle("Enter the date permission was given.");
    }

    [Fact]
    public void ROW_077_A_granted_right_reads_expired_the_day_after_it_ends()
    {
        RightOfEntryRules.Effective(RightOfEntryStatus.Granted, Today, Today).Should().Be("Granted", "it still holds on its last day");
        RightOfEntryRules.Effective(RightOfEntryStatus.Granted, Today.AddDays(-1), Today).Should().Be("Expired");
        RightOfEntryRules.Effective(RightOfEntryStatus.Granted, null, Today).Should().Be("Granted", "no end date means it does not lapse");
        RightOfEntryRules.Effective(RightOfEntryStatus.Revoked, Today.AddDays(-10), Today).Should().Be("Revoked", "only a granted right expires");
    }

    [Theory]
    [InlineData(0, 30, true)]    // ends today
    [InlineData(30, 30, true)]   // ends on the last day of the window
    [InlineData(31, 30, false)]  // just outside
    [InlineData(-1, 30, false)]  // already over
    public void ROW_077_Expiring_soon_covers_today_through_the_window_inclusive(int endsInDays, int window, bool expected) =>
        RightOfEntryRules.ExpiresSoon(RightOfEntryStatus.Granted, Today.AddDays(endsInDays), Today, window).Should().Be(expected);

    [Fact]
    public void ROW_077_Only_granted_rights_with_an_end_date_expire_soon()
    {
        RightOfEntryRules.ExpiresSoon(RightOfEntryStatus.Granted, null, Today, 30).Should().BeFalse();
        RightOfEntryRules.ExpiresSoon(RightOfEntryStatus.Denied, Today.AddDays(5), Today, 30).Should().BeFalse();
    }

    // ── ROW-239 ──

    private static PlanReviewGate.ReviewFact Review(ReviewDiscipline d, ReviewOutcome o, int daysAgo = 0, int minute = 0) =>
        new(d, o, Today.AddDays(-daysAgo), new DateTime(2026, 3, 15, 12, minute, 0, DateTimeKind.Utc));

    [Fact]
    public void ROW_239_With_the_gate_off_nothing_is_missing()
    {
        PlanReviewGate.Missing(false, Array.Empty<PlanReviewGate.ReviewFact>()).Should().BeEmpty();
    }

    [Fact]
    public void ROW_239_With_the_gate_on_both_disciplines_are_missing_until_each_approves()
    {
        PlanReviewGate.Missing(true, Array.Empty<PlanReviewGate.ReviewFact>()).Should().Equal(ReviewDiscipline.Engineer, ReviewDiscipline.Surveyor);
        PlanReviewGate.Missing(true, new[] { Review(ReviewDiscipline.Engineer, ReviewOutcome.Approved) }).Should().Equal(ReviewDiscipline.Surveyor);
        PlanReviewGate.Missing(true, new[] { Review(ReviewDiscipline.Engineer, ReviewOutcome.Approved), Review(ReviewDiscipline.Surveyor, ReviewOutcome.Approved) }).Should().BeEmpty();
    }

    [Fact]
    public void ROW_239_Only_the_latest_review_of_a_discipline_counts()
    {
        var reviews = new[]
        {
            Review(ReviewDiscipline.Engineer, ReviewOutcome.Approved, daysAgo: 10),
            Review(ReviewDiscipline.Engineer, ReviewOutcome.ChangesRequired, daysAgo: 2),   // a later review withdraws the approval
            Review(ReviewDiscipline.Surveyor, ReviewOutcome.ChangesRequired, daysAgo: 5),
            Review(ReviewDiscipline.Surveyor, ReviewOutcome.Approved, daysAgo: 1),
        };
        PlanReviewGate.Missing(true, reviews).Should().Equal(ReviewDiscipline.Engineer);
    }

    [Fact]
    public void ROW_239_Two_reviews_on_the_same_day_are_ordered_by_when_they_were_recorded()
    {
        var reviews = new[]
        {
            Review(ReviewDiscipline.Engineer, ReviewOutcome.Approved, minute: 5),
            Review(ReviewDiscipline.Engineer, ReviewOutcome.ChangesRequired, minute: 30),
            Review(ReviewDiscipline.Surveyor, ReviewOutcome.Approved),
        };
        PlanReviewGate.Missing(true, reviews).Should().Equal(ReviewDiscipline.Engineer);
    }

    [Fact]
    public void ROW_239_Missing_disciplines_are_worded_for_a_message()
    {
        PlanReviewGate.Describe(new[] { ReviewDiscipline.Engineer, ReviewDiscipline.Surveyor }).Should().Be("engineer and surveyor");
        PlanReviewGate.Describe(new[] { ReviewDiscipline.Surveyor }).Should().Be("surveyor");
    }

    private static readonly IReadOnlyList<WorkflowStepInfo> Steps = new[]
    {
        new WorkflowStepInfo("setup", "Parcel set up", false),
        new WorkflowStepInfo(PlanReviewGate.StepKey, "Title & survey", false),
        new WorkflowStepInfo("appraisal", "Appraisal", false),
        new WorkflowStepInfo("offer", "Offer", true),
    };

    private static StepMoveResult Move(string from, string to, string? missing, bool mayOverride = false, string? reason = null) =>
        StepGate.CanAdvance(Steps, from, to, true, mayOverride, reason, missing);

    [Fact]
    public void ROW_239_A_parcel_cannot_leave_the_review_step_while_a_review_is_missing()
    {
        var r = Move(PlanReviewGate.StepKey, "appraisal", "surveyor");
        r.Outcome.Should().Be(StepMoveOutcome.BlockedByPlanReview);
        r.Message.Should().Contain("surveyor").And.Contain("Title & survey");
    }

    [Fact]
    public void ROW_239_An_override_cannot_get_past_the_plan_review_gate()
    {
        Move(PlanReviewGate.StepKey, "offer", "engineer and surveyor", mayOverride: true, reason: "urgent").Outcome.Should().Be(StepMoveOutcome.BlockedByPlanReview);
        Move("setup", "appraisal", "engineer", mayOverride: true, reason: "skip ahead").Outcome.Should().Be(StepMoveOutcome.BlockedByPlanReview, "skipping over the review step is still blocked");
    }

    [Fact]
    public void ROW_239_Moves_that_do_not_pass_the_review_step_are_not_held()
    {
        Move("setup", PlanReviewGate.StepKey, "engineer").Outcome.Should().Be(StepMoveOutcome.Allowed, "arriving at the step is fine");
        Move("appraisal", PlanReviewGate.StepKey, "engineer", mayOverride: true, reason: "went back").Outcome.Should().Be(StepMoveOutcome.AllowedWithOverride, "moving back is not held");
        Move("appraisal", "offer", "engineer").Outcome.Should().Be(StepMoveOutcome.Allowed, "already past the review step");
    }

    [Fact]
    public void ROW_239_With_nothing_missing_the_review_step_is_left_normally()
    {
        Move(PlanReviewGate.StepKey, "appraisal", null).Outcome.Should().Be(StepMoveOutcome.Allowed);
    }

    // ── ROW-080 / 240 ──

    [Fact]
    public void ROW_240_Nobody_decides_their_own_submission()
    {
        var me = Guid.NewGuid();
        ApprovalRoute.IsOwnSubmission(me, me).Should().BeTrue();
        ApprovalRoute.IsOwnSubmission(Guid.NewGuid(), me).Should().BeFalse();
        ApprovalRoute.IsOwnSubmission(null, me).Should().BeFalse("a draft has no submitter");
    }

    [Theory]
    [InlineData(ReportStatus.Draft, true, true, false, true)]
    [InlineData(ReportStatus.Rejected, true, true, false, true)]
    [InlineData(ReportStatus.Submitted, false, false, true, false)]
    [InlineData(ReportStatus.Approved, false, false, false, false)]
    public void ROW_240_A_report_is_changed_and_submitted_only_before_a_decision_and_decided_only_once_submitted(
        ReportStatus status, bool canEdit, bool canSubmit, bool canDecide, bool canDelete)
    {
        ApprovalRoute.CanEdit(status).Should().Be(canEdit);
        ApprovalRoute.CanSubmit(status).Should().Be(canSubmit);
        ApprovalRoute.CanDecide(status).Should().Be(canDecide);
        ApprovalRoute.CanDelete(status).Should().Be(canDelete);
    }

    // ── ROW-241 ──

    private static PlanParcelInput Parcel(string number, decimal area, params PlanMilestone[] milestones) =>
        new(Guid.NewGuid(), number, null, area, "Active", "Appraisal", milestones);

    [Fact]
    public void ROW_241_An_empty_project_has_an_empty_plan()
    {
        var plan = AcquisitionPlan.Build(Array.Empty<PlanParcelInput>(), Array.Empty<PlanBudgetYear>(), null, 0m, Today);
        plan.ParcelCount.Should().Be(0);
        plan.FirstDueOn.Should().BeNull();
        plan.LastPlannedCloseOn.Should().BeNull();
        plan.Budget.Variance.Should().BeNull();
        plan.Budget.OverBudget.Should().BeFalse();
    }

    [Fact]
    public void ROW_241_Each_parcel_shows_its_next_open_milestone_and_whether_it_is_overdue()
    {
        var parcel = Parcel("001", 100m,
            new PlanMilestone(MilestoneNames.ParcelSetUp, null, Today.AddDays(-50)),
            new PlanMilestone(MilestoneNames.OfferDelivered, Today.AddDays(-3), null),
            new PlanMilestone(MilestoneNames.NoticeToOwner, Today.AddDays(-10), null),
            new PlanMilestone(MilestoneNames.Closed, Today.AddDays(90), null));

        var row = AcquisitionPlan.Build(new[] { parcel }, Array.Empty<PlanBudgetYear>(), null, 0m, Today).Parcels.Single();

        row.NextMilestone.Should().Be(MilestoneNames.NoticeToOwner, "the earliest due date wins");
        row.NextDueOn.Should().Be(Today.AddDays(-10));
        row.IsOverdue.Should().BeTrue();
        row.PlannedCloseOn.Should().Be(Today.AddDays(90));
        row.MilestonesDone.Should().Be(1);
        row.MilestonesTotal.Should().Be(4);
        row.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void ROW_241_A_milestone_due_today_is_not_overdue_and_one_without_a_date_is_not_planned()
    {
        var parcel = Parcel("001", 1m, new PlanMilestone(MilestoneNames.NoticeToOwner, Today, null), new PlanMilestone(MilestoneNames.OfferDelivered, null, null));
        var row = AcquisitionPlan.Build(new[] { parcel }, Array.Empty<PlanBudgetYear>(), null, 0m, Today).Parcels.Single();
        row.IsOverdue.Should().BeFalse();
        row.NextMilestone.Should().Be(MilestoneNames.NoticeToOwner);

        var undated = Parcel("002", 1m, new PlanMilestone(MilestoneNames.OfferDelivered, null, null));
        AcquisitionPlan.Build(new[] { undated }, Array.Empty<PlanBudgetYear>(), null, 0m, Today).Parcels.Single().NextMilestone.Should().BeNull();
    }

    [Fact]
    public void ROW_241_A_closed_parcel_uses_its_completion_date_and_counts_as_closed()
    {
        var closed = Parcel("003", 50m, new PlanMilestone(MilestoneNames.Closed, Today.AddDays(-80), Today.AddDays(-60)));
        var plan = AcquisitionPlan.Build(new[] { closed }, Array.Empty<PlanBudgetYear>(), null, 0m, Today);
        plan.Parcels.Single().IsClosed.Should().BeTrue();
        plan.Parcels.Single().PlannedCloseOn.Should().Be(Today.AddDays(-60));
        plan.ClosedCount.Should().Be(1);
        plan.OverdueCount.Should().Be(0);
    }

    [Fact]
    public void ROW_241_The_project_span_runs_from_the_first_due_date_to_the_last_planned_close_and_parcels_sort_by_number()
    {
        var a = Parcel("010", 10m, new PlanMilestone(MilestoneNames.NoticeToOwner, Today.AddDays(5), null), new PlanMilestone(MilestoneNames.Closed, Today.AddDays(200), null));
        var b = Parcel("002", 20m, new PlanMilestone(MilestoneNames.NoticeToOwner, Today.AddDays(-4), null), new PlanMilestone(MilestoneNames.Closed, Today.AddDays(120), null));

        var plan = AcquisitionPlan.Build(new[] { a, b }, Array.Empty<PlanBudgetYear>(), null, 0m, Today);

        plan.Parcels.Select(p => p.Number).Should().Equal("002", "010");
        plan.AreaSqFt.Should().Be(30m);
        plan.FirstDueOn.Should().Be(Today.AddDays(-4));
        plan.LastPlannedCloseOn.Should().Be(Today.AddDays(200));
        plan.OverdueCount.Should().Be(1);
    }

    [Theory]
    [InlineData(1_000_000, 900_000, 100_000, false)]
    [InlineData(1_000_000, 1_000_000, 0, false)]
    [InlineData(1_000_000, 1_250_000, -250_000, true)]
    public void ROW_241_The_budget_variance_is_the_budget_less_the_current_estimate(int budget, int estimate, int variance, bool over)
    {
        var plan = AcquisitionPlan.Build(Array.Empty<PlanParcelInput>(), new[] { new PlanBudgetYear(2026, budget / 2), new PlanBudgetYear(2027, budget - budget / 2) }, estimate, 750_000m, Today);
        plan.Budget.TotalBudget.Should().Be(budget);
        plan.Budget.Variance.Should().Be(variance);
        plan.Budget.OverBudget.Should().Be(over);
        plan.Budget.AuthorizedTotal.Should().Be(750_000m);
    }

    [Fact]
    public void ROW_241_Without_a_budget_or_an_estimate_there_is_no_variance_to_report()
    {
        AcquisitionPlan.Build(Array.Empty<PlanParcelInput>(), Array.Empty<PlanBudgetYear>(), 500m, 0m, Today).Budget.Variance.Should().BeNull("no budget has been set");
        AcquisitionPlan.Build(Array.Empty<PlanParcelInput>(), new[] { new PlanBudgetYear(2026, 500m) }, null, 0m, Today).Budget.Variance.Should().BeNull("no estimate has been made");
    }
}
