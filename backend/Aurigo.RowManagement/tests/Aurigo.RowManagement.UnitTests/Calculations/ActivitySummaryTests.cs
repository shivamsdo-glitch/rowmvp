using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Domain.Enums;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Calculations;

/// <summary>Phase 1.5 dashboard (ADR 0022): turning a person's audit rows into "what I did".</summary>
public class ActivitySummaryTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 15, 30, 0, DateTimeKind.Utc);
    private static ActivityEntry E(string type, Guid id, AuditAction a, DateTime at) => new(type, id, a, at);

    [Fact]
    public void No_audit_rows_means_zero_everything_and_a_full_zero_filled_chart()
    {
        var t = ActivitySummary.Summarize(Array.Empty<ActivityEntry>(), Now);
        t.Total.Should().Be(0);
        t.LastActiveAt.Should().BeNull();
        t.Days.Should().HaveCount(7).And.OnlyContain(d => d.Count == 0);
        t.Days[^1].Date.Should().Be(new DateOnly(2026, 10, 21), "the chart ends today");
        t.Days[0].Date.Should().Be(new DateOnly(2026, 10, 15));
        t.ByKind.Should().BeEmpty();
        t.Recent.Should().BeEmpty();
    }

    [Fact]
    public void A_save_that_changes_five_fields_is_one_action_not_five()
    {
        var id = Guid.NewGuid();
        var rows = Enumerable.Range(0, 5).Select(i => E("ParcelAppraisal", id, AuditAction.Update, Now.AddSeconds(-i * 12))).ToList();
        var t = ActivitySummary.Summarize(rows, Now);
        t.Updated.Should().Be(1);
        t.Total.Should().Be(1);
    }

    [Fact]
    public void Rows_of_one_save_that_straddle_a_minute_boundary_are_still_one_action()
    {
        var id = Guid.NewGuid();
        var t = ActivitySummary.Summarize(new[]
        {
            E("Parcel", id, AuditAction.Update, new DateTime(2026, 10, 21, 15, 29, 59, DateTimeKind.Utc)),
            E("Parcel", id, AuditAction.Update, new DateTime(2026, 10, 21, 15, 30, 0, DateTimeKind.Utc)),
        }, Now);
        t.Updated.Should().Be(1);
    }

    [Fact]
    public void The_same_record_changed_in_different_minutes_counts_each_time()
    {
        var id = Guid.NewGuid();
        var t = ActivitySummary.Summarize(new[]
        {
            E("Parcel", id, AuditAction.Update, Now.AddMinutes(-1)),
            E("Parcel", id, AuditAction.Update, Now.AddMinutes(-10)),
            E("Parcel", id, AuditAction.Update, Now.AddHours(-3)),
        }, Now);
        t.Updated.Should().Be(3);
    }

    [Fact]
    public void Creates_updates_and_deletes_are_counted_apart()
    {
        var t = ActivitySummary.Summarize(new[]
        {
            E("Parcel", Guid.NewGuid(), AuditAction.Create, Now.AddHours(-1)),
            E("Parcel", Guid.NewGuid(), AuditAction.Create, Now.AddHours(-2)),
            E("Project", Guid.NewGuid(), AuditAction.Update, Now.AddHours(-3)),
            E("ParcelNote", Guid.NewGuid(), AuditAction.Delete, Now.AddHours(-4)),
        }, Now);
        (t.Created, t.Updated, t.Deleted, t.Total).Should().Be((2, 1, 1, 4));
        t.ByKind.Select(k => k.Kind).Should().ContainInOrder("Parcel", "Parcel note", "Project");
        t.ByKind[0].Should().Be(new ActivityKind("Parcel", 2, 0, 0));
    }

    [Fact]
    public void Rows_older_than_the_window_or_in_the_future_are_ignored()
    {
        var t = ActivitySummary.Summarize(new[]
        {
            E("Parcel", Guid.NewGuid(), AuditAction.Create, Now.AddDays(-31)),
            E("Parcel", Guid.NewGuid(), AuditAction.Create, Now.AddMinutes(5)),
            E("Parcel", Guid.NewGuid(), AuditAction.Create, Now.AddDays(-29)),
        }, Now, windowDays: 30);
        t.Created.Should().Be(1);
    }

    [Fact]
    public void Bookkeeping_records_are_not_work_the_person_did()
    {
        var t = ActivitySummary.Summarize(
            ActivitySummary.IgnoredTypes.Select(type => E(type, Guid.NewGuid(), AuditAction.Update, Now.AddMinutes(-2))).Append(E("Parcel", Guid.NewGuid(), AuditAction.Create, Now.AddMinutes(-2))), Now);
        t.Total.Should().Be(1);
        t.ByKind.Should().ContainSingle().Which.Kind.Should().Be("Parcel");
    }

    [Fact]
    public void The_chart_puts_each_action_on_the_day_it_happened_and_only_covers_the_last_seven_days()
    {
        var t = ActivitySummary.Summarize(new[]
        {
            E("Parcel", Guid.NewGuid(), AuditAction.Create, Now),
            E("Parcel", Guid.NewGuid(), AuditAction.Create, Now.AddMinutes(-20)),
            E("Parcel", Guid.NewGuid(), AuditAction.Create, Now.AddDays(-2)),
            E("Parcel", Guid.NewGuid(), AuditAction.Create, Now.AddDays(-10)),
        }, Now);
        t.Days.Single(d => d.Date == new DateOnly(2026, 10, 21)).Count.Should().Be(2);
        t.Days.Single(d => d.Date == new DateOnly(2026, 10, 19)).Count.Should().Be(1);
        t.Days.Sum(d => d.Count).Should().Be(3, "the action ten days ago is in the total but not on a 7-day chart");
        t.Total.Should().Be(4);
    }

    [Fact]
    public void Recent_lists_the_latest_first_and_stops_at_the_limit_and_last_active_is_the_newest()
    {
        var rows = Enumerable.Range(1, 15).Select(i => E("Parcel", Guid.NewGuid(), AuditAction.Create, Now.AddMinutes(-i * 5))).ToList();
        var t = ActivitySummary.Summarize(rows, Now, recentCount: 4);
        t.Recent.Should().HaveCount(4);
        t.Recent.Select(r => r.At).Should().BeInDescendingOrder();
        t.LastActiveAt.Should().Be(Now.AddMinutes(-5));
    }

    [Theory]
    [InlineData("Parcel", "Parcel")]
    [InlineData("ParcelAppraisalAmount", "Parcel appraisal amount")]
    [InlineData("RightOfEntry", "Right of entry")]
    [InlineData("NegotiationRightOfEntry", "Negotiation right of entry")]
    public void Record_types_read_as_plain_words(string clr, string label) => ActivitySummary.Label(clr).Should().Be(label);
}
