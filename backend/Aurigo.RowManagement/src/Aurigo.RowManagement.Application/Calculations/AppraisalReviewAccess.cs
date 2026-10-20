using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Application.Calculations;

/// <summary>The review part of an appraisal: the fields only the review roles may change (ADR 0018, judgement call 2 of the 2026-10-19 matrix).</summary>
public readonly record struct AppraisalReviewValues(
    string? ReviewAppraiser, string? ReviewType, DateOnly? AppraisalReceivedDate, decimal? ApprovedJustCompensation, DateOnly? ReviewCompletedDate,
    DateOnly? ReviewSubmittedDate, string? ReviewRecommendation, decimal? ReviewRecommendedValue);

/// <summary>
/// Who may write the review half of an appraisal. The Appraiser fills in the appraisal and may send the review fields back as they are, but
/// not change them; the Appraisal Approver reviews and sets the approved just compensation. Pure: roles in, answer out.
/// </summary>
public static class AppraisalReviewAccess
{
    /// <summary>The roles that review (and Admin, who can do everything).</summary>
    public static readonly IReadOnlyList<string> ReviewRoles = new[] { RoleNames.AppraisalApprover, RoleNames.Admin };

    public static bool MayChangeReview(IEnumerable<string> roles) => roles.Any(r => ReviewRoles.Contains(r, StringComparer.Ordinal));

    /// <summary>Whether sending <paramref name="incoming"/> over <paramref name="existing"/> changes any review field (blank text counts as nothing).</summary>
    public static bool ReviewChanged(AppraisalReviewValues existing, AppraisalReviewValues incoming) => Normalize(existing) != Normalize(incoming);

    private static AppraisalReviewValues Normalize(AppraisalReviewValues v) =>
        v with { ReviewAppraiser = Blank(v.ReviewAppraiser), ReviewType = Blank(v.ReviewType), ReviewRecommendation = Blank(v.ReviewRecommendation) };

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
