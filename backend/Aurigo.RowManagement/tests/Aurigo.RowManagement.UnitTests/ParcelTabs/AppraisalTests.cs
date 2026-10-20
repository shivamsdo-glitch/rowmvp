using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.ParcelTabs;
using Aurigo.RowManagement.Domain.Common;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.ParcelTabs;

/// <summary>Phase 1.5 slice 6c (ADR 0018): Appraisal rules that need no database. Matches AT-AP-40, AT-AA-40, AT-X-51.</summary>
public class AppraisalTests
{
    private static AddAppraisalCommand Cmd(
        DateOnly? requested = null, DateOnly? received = null, DateOnly? jatSubmitted = null, DateOnly? jatApproved = null,
        IReadOnlyList<AppraisalAmountInput>? amounts = null, decimal? approved = null, string? appraiser = null) =>
        new(Guid.NewGuid(), "Full", appraiser, false, null, requested, received, null, null, null, null, amounts, null, null, null, approved, null, null, null, null, jatSubmitted, jatApproved, false);

    private static bool Valid(AddAppraisalCommand c) => new AddAppraisalCommandValidator().Validate(c).IsValid;

    [Fact]
    public void AT_AP_40_An_appraisal_cannot_be_received_before_it_is_requested()
    {
        Valid(Cmd(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1))).Should().BeTrue();
        Valid(Cmd(new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 1))).Should().BeFalse();
        Valid(Cmd(received: new DateOnly(2026, 2, 1))).Should().BeTrue("one date alone is fine");
    }

    [Fact]
    public void AT_AP_40_The_JAT_cannot_approve_before_the_appraisal_is_submitted()
    {
        Valid(Cmd(jatSubmitted: new DateOnly(2026, 3, 1), jatApproved: new DateOnly(2026, 3, 1))).Should().BeTrue();
        Valid(Cmd(jatSubmitted: new DateOnly(2026, 3, 2), jatApproved: new DateOnly(2026, 3, 1))).Should().BeFalse();
    }

    [Fact]
    public void AT_AP_40_Amount_rows_need_a_type_and_a_non_negative_amount_and_there_are_at_most_fifty()
    {
        Valid(Cmd(amounts: new[] { new AppraisalAmountInput("Land", null, 0m) })).Should().BeTrue();
        Valid(Cmd(amounts: new[] { new AppraisalAmountInput("", null, 1m) })).Should().BeFalse();
        Valid(Cmd(amounts: new[] { new AppraisalAmountInput("Land", null, -0.01m) })).Should().BeFalse();
        Valid(Cmd(amounts: new[] { new AppraisalAmountInput("Land", new string('d', 501), 1m) })).Should().BeFalse();
        Valid(Cmd(amounts: Enumerable.Range(0, 50).Select(_ => new AppraisalAmountInput("Land", null, 1m)).ToList())).Should().BeTrue();
        Valid(Cmd(amounts: Enumerable.Range(0, 51).Select(_ => new AppraisalAmountInput("Land", null, 1m)).ToList())).Should().BeFalse();
    }

    [Fact]
    public void AT_AA_40_The_approved_just_compensation_is_zero_or_more_and_text_has_limits()
    {
        Valid(Cmd(approved: 0m)).Should().BeTrue();
        Valid(Cmd(approved: -1m)).Should().BeFalse();
        Valid(Cmd(appraiser: new string('a', 200))).Should().BeTrue();
        Valid(Cmd(appraiser: new string('a', 201))).Should().BeFalse();
    }

    [Fact]
    public void AT_AA_40_Only_the_approver_and_admin_may_change_the_review()
    {
        AppraisalReviewAccess.MayChangeReview(new[] { RoleNames.AppraisalApprover }).Should().BeTrue();
        AppraisalReviewAccess.MayChangeReview(new[] { RoleNames.Admin }).Should().BeTrue();
        AppraisalReviewAccess.MayChangeReview(new[] { RoleNames.Appraiser, RoleNames.ProjectManager }).Should().BeFalse();
        AppraisalReviewAccess.MayChangeReview(Array.Empty<string>()).Should().BeFalse();
    }

    [Fact]
    public void AT_AA_40_Sending_the_review_back_as_it_is_is_not_a_change_and_blank_text_counts_as_nothing()
    {
        var existing = new AppraisalReviewValues("Rita", "Desk", new DateOnly(2026, 2, 1), 5m, null, null, "Approve", 6m);
        AppraisalReviewAccess.ReviewChanged(existing, existing).Should().BeFalse();
        AppraisalReviewAccess.ReviewChanged(existing, existing with { ReviewAppraiser = " Rita " }).Should().BeFalse("surrounding spaces are trimmed");
        AppraisalReviewAccess.ReviewChanged(existing, existing with { ApprovedJustCompensation = 7m }).Should().BeTrue();
        AppraisalReviewAccess.ReviewChanged(existing, default(AppraisalReviewValues)).Should().BeTrue("blanking the review is a change");
        AppraisalReviewAccess.ReviewChanged(default(AppraisalReviewValues), default(AppraisalReviewValues) with { ReviewType = "  " }).Should().BeFalse();
    }
}
