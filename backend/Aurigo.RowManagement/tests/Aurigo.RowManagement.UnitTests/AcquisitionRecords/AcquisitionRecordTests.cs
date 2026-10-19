using Aurigo.RowManagement.Application.AcquisitionPlans;
using Aurigo.RowManagement.Application.AcquisitionRequests;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.PlanReviews;
using Aurigo.RowManagement.Application.RightsOfEntry;
using Aurigo.RowManagement.Application.StaffReports;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.UnitTests.AcquisitionRecords;

/// <summary>Rights of entry, plan-review gate, staff reports, acquisition requests and the plan (ROW-077, 080, 239, 240, 241).</summary>
public class AcquisitionRecordTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateOnly Today = new(2026, 3, 15);
    private static readonly Guid PmId = new("aaaaaaaa-0000-0000-0000-0000000000c1");
    private static readonly Guid FinanceId = new("aaaaaaaa-0000-0000-0000-0000000000c2");
    private static readonly string[] AllRoles = { "ProjectManager", "TitleResearcher", "Appraiser", "Negotiator", "Finance", "Admin" };

    private sealed class Ctx : IAsyncDisposable
    {
        public RowDbContext Db { get; }
        public FakeUser User { get; }
        private readonly PagePermissionService _perms;
        private readonly ParcelService _parcels;
        private readonly TimeProvider _clock = new FixedClock();

        public Ctx(string store, Guid tenant, string role, Guid? userId = null)
        {
            User = new FakeUser(userId ?? PmId, role);
            Db = TestDb.Create(store, tenant, User.Id);
            _perms = new PagePermissionService(Db, User);
            _parcels = new ParcelService(Db, User, _clock);
        }

        public ListParcelRightsOfEntryQueryHandler ListRoe => new(Db, _perms, _parcels);
        public AddRightOfEntryCommandHandler AddRoe => new(Db, _perms, _parcels);
        public UpdateRightOfEntryCommandHandler UpdateRoe => new(Db, _perms, _parcels);
        public RemoveRightOfEntryCommandHandler RemoveRoe => new(Db, _perms, _parcels);
        public RightsOfEntryReportQueryHandler RoeReport => new(Db, _perms, _parcels);
        public GetParcelPlanReviewsQueryHandler Reviews => new(Db, _perms, _parcels);
        public AddPlanReviewCommandHandler AddReview => new(Db, _perms, _parcels);
        public RemovePlanReviewCommandHandler RemoveReview => new(Db, _perms, _parcels);
        public GetPlanReviewGateQueryHandler GetGate => new(Db, _perms);
        public SetPlanReviewGateCommandHandler SetGate => new(Db, _perms);
        public AdvanceParcelStepCommandHandler Advance => new(Db, _perms, _parcels);
        public ListStaffReportsQueryHandler Reports => new(Db, _perms, _parcels, User);
        public CreateStaffReportCommandHandler CreateReport => new(Db, _perms, _parcels, User);
        public UpdateStaffReportCommandHandler UpdateReport => new(Db, _perms, _parcels, User);
        public SubmitStaffReportCommandHandler SubmitReport => new(Db, _perms, _parcels, User, _clock);
        public DecideStaffReportCommandHandler DecideReport => new(Db, _perms, _parcels, User, _clock);
        public DeleteStaffReportCommandHandler DeleteReport => new(Db, _perms, _parcels);
        public ListAcquisitionRequestsQueryHandler Requests => new(Db, _perms, User);
        public SubmitAcquisitionRequestCommandHandler Submit => new(Db, _perms, User);
        public DecideAcquisitionRequestCommandHandler Decide => new(Db, _perms, User, _clock);
        public WithdrawAcquisitionRequestCommandHandler Withdraw => new(Db, _perms, User);
        public GetAcquisitionPlanQueryHandler Plan => new(Db, _perms, _parcels);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static async Task<string> Provision()
    {
        var store = TestDb.NewStoreName();
        await using var db = TestDb.Create(store, tenant: null);
        await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantA);
        await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantB);
        return store;
    }

    private static async Task<(Guid Parcel, Guid Project)> AddParcel(string store, Guid tenant, string step = "setup", ParcelState state = ParcelState.Active)
    {
        await using var db = TestDb.Create(store, tenant, TestDb.UserA);
        var project = new Project { Number = "P-" + Guid.NewGuid().ToString("N")[..4], Name = "Acquisition", OversightType = OversightType.Local };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var status = await db.ParcelStatuses.OrderBy(s => s.SortOrder).FirstAsync();
        var parcel = new Parcel { ProjectId = project.Id, Number = "001", StatusId = status.Id, CurrentStepKey = step, AreaSqFt = 10, State = state };
        db.Parcels.Add(parcel);
        await db.SaveChangesAsync();
        return (parcel.Id, project.Id);
    }

    private static async Task<T> Thrown<T>(Func<Task> act) where T : Exception => (await act.Should().ThrowAsync<T>()).Which;

    private static AddRightOfEntryCommand Granted(Guid parcel, int grantedDaysAgo = 10, int? endsInDays = 30) =>
        new(parcel, RightOfEntryStatus.Granted, Today.AddDays(-20), Today.AddDays(-grantedDaysAgo), endsInDays is { } e ? Today.AddDays(e) : null, "  Alex Marlowe ", "Survey", null);

    // ── right of entry (ROW-077) ──

    [Fact]
    public async Task ROW_077_A_right_of_entry_is_added_changed_and_removed_on_a_parcel()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        var added = await c.AddRoe.Handle(Granted(parcel), default);
        added.GrantedBy.Should().Be("Alex Marlowe", "text is trimmed");
        added.EffectiveStatus.Should().Be("Granted");

        var changed = await c.UpdateRoe.Handle(new UpdateRightOfEntryCommand(parcel, added.Id, RightOfEntryStatus.Revoked, Today.AddDays(-20), Today.AddDays(-10), Today.AddDays(30), "Alex Marlowe", "Survey", "Owner withdrew"), default);
        changed.Status.Should().Be(RightOfEntryStatus.Revoked);
        (await c.ListRoe.Handle(new ListParcelRightsOfEntryQuery(parcel), default)).Should().ContainSingle().Which.Notes.Should().Be("Owner withdrew");

        await c.RemoveRoe.Handle(new RemoveRightOfEntryCommand(parcel, added.Id), default);
        (await c.ListRoe.Handle(new ListParcelRightsOfEntryQuery(parcel), default)).Should().BeEmpty();
        await Thrown<NotFoundException>(() => c.RemoveRoe.Handle(new RemoveRightOfEntryCommand(parcel, added.Id), default));
    }

    [Fact]
    public async Task ROW_077_A_granted_right_without_a_granted_date_or_a_future_date_is_refused_with_the_field()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        var noDate = await Thrown<ValidationException>(() => c.AddRoe.Handle(new AddRightOfEntryCommand(parcel, RightOfEntryStatus.Granted, null, null, null, null, null, null), default));
        noDate.Errors.Should().Contain(e => e.PropertyName == "GrantedOn");
        var future = await Thrown<ValidationException>(() => c.AddRoe.Handle(new AddRightOfEntryCommand(parcel, RightOfEntryStatus.Requested, Today.AddDays(2), null, null, null, null, null), default));
        future.Errors.Should().Contain(e => e.PropertyName == "RequestedOn");
        (await c.Db.RightsOfEntry.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ROW_077_A_right_past_its_end_date_reads_expired_in_the_project_time_zone()
    {
        var store = await Provision();
        var (parcel, project) = await AddParcel(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var ended = await c.AddRoe.Handle(new AddRightOfEntryCommand(parcel, RightOfEntryStatus.Granted, null, Today.AddDays(-60), Today.AddDays(-1), null, null, null), default);

        ended.EffectiveStatus.Should().Be("Expired");

        // Auckland is a day ahead of UTC at 12:00 UTC on the 15th; a right ending "today" there has not ended yet in UTC but has in Auckland.
        var edge = await c.AddRoe.Handle(new AddRightOfEntryCommand(parcel, RightOfEntryStatus.Granted, null, Today.AddDays(-60), Today, null, null, null), default);
        edge.EffectiveStatus.Should().Be("Granted");
        var proj = await c.Db.Projects.FirstAsync(p => p.Id == project);
        proj.TimeZoneId = AgencyTime.Supported.First(z => z.Id.Contains("Auckland", StringComparison.OrdinalIgnoreCase) || z.Id.Contains("Honolulu", StringComparison.OrdinalIgnoreCase)).Id;
        await c.Db.SaveChangesAsync();
        var shown = (await c.ListRoe.Handle(new ListParcelRightsOfEntryQuery(parcel), default)).Single(r => r.Id == edge.Id);
        var zoneToday = AgencyTime.TodayIn(proj.TimeZoneId, new FixedClock().GetUtcNow());
        shown.EffectiveStatus.Should().Be(zoneToday > Today ? "Expired" : "Granted");
    }

    [Fact]
    public async Task ROW_077_The_report_lists_every_right_filters_by_status_and_expiry_and_skips_superseded_parcels()
    {
        var store = await Provision();
        var (parcelA, projectA) = await AddParcel(store, TestDb.TenantA);
        var (parcelB, _) = await AddParcel(store, TestDb.TenantA);
        var (superseded, _) = await AddParcel(store, TestDb.TenantA, state: ParcelState.Split);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        await c.AddRoe.Handle(Granted(parcelA, endsInDays: 10), default);
        await c.AddRoe.Handle(Granted(parcelA, endsInDays: 200), default);
        await c.AddRoe.Handle(new AddRightOfEntryCommand(parcelA, RightOfEntryStatus.Granted, null, Today.AddDays(-90), Today.AddDays(-5), null, null, null), default);
        await c.AddRoe.Handle(new AddRightOfEntryCommand(parcelB, RightOfEntryStatus.Requested, Today.AddDays(-1), null, null, null, "Inspection", null), default);
        c.Db.RightsOfEntry.Add(new RightOfEntry { ParcelId = superseded, Status = RightOfEntryStatus.Requested });
        await c.Db.SaveChangesAsync();

        var all = await c.RoeReport.Handle(new RightsOfEntryReportQuery(null, null, null), default);
        all.Total.Should().Be(4, "the superseded parcel's right is not listed");

        (await c.RoeReport.Handle(new RightsOfEntryReportQuery(projectA, null, null), default)).Total.Should().Be(3);
        (await c.RoeReport.Handle(new RightsOfEntryReportQuery(null, "Requested", null), default)).Items.Should().ContainSingle().Which.ParcelId.Should().Be(parcelB);
        (await c.RoeReport.Handle(new RightsOfEntryReportQuery(null, "Expired", null), default)).Items.Should().ContainSingle().Which.EffectiveStatus.Should().Be("Expired");
        (await c.RoeReport.Handle(new RightsOfEntryReportQuery(null, "Granted", null), default)).Total.Should().Be(2, "an expired right is not still granted");
        var soon = await c.RoeReport.Handle(new RightsOfEntryReportQuery(null, null, 30), default);
        soon.Items.Should().ContainSingle().Which.DaysUntilExpiry.Should().Be(10);
    }

    [Fact]
    public async Task ROW_077_Rights_of_entry_follow_the_parcels_permissions_for_every_role()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using (var admin = new Ctx(store, TestDb.TenantA, RoleNames.Admin)) await admin.AddRoe.Handle(Granted(parcel), default);

        foreach (var role in AllRoles)
        {
            await using var c = new Ctx(store, TestDb.TenantA, role);
            (await c.ListRoe.Handle(new ListParcelRightsOfEntryQuery(parcel), default)).Should().NotBeNull($"{role} can read parcels");
            var write = () => c.AddRoe.Handle(Granted(parcel), default);
            if (role is RoleNames.ProjectManager or RoleNames.Admin) await write.Should().NotThrowAsync(role);
            else await write.Should().ThrowAsync<ForbiddenException>(role);
        }
    }

    [Fact]
    public async Task ROW_077_A_superseded_parcel_takes_no_new_right_and_another_tenant_cannot_see_any()
    {
        var store = await Provision();
        var (gone, _) = await AddParcel(store, TestDb.TenantA, state: ParcelState.Split);
        var (live, _) = await AddParcel(store, TestDb.TenantA);
        await using var a = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var conflict = await Thrown<ConflictException>(() => a.AddRoe.Handle(Granted(gone), default));
        conflict.Code.Should().Be("parcel_not_active");

        await a.AddRoe.Handle(Granted(live), default);
        await using var other = new Ctx(store, TestDb.TenantB, RoleNames.Admin);
        await Thrown<NotFoundException>(() => other.ListRoe.Handle(new ListParcelRightsOfEntryQuery(live), default));
        (await other.RoeReport.Handle(new RightsOfEntryReportQuery(null, null, null), default)).Total.Should().Be(0);
    }

    // ── plan-review gate (ROW-239) ──

    [Fact]
    public async Task ROW_239_The_gate_is_off_until_an_administrator_turns_it_on_and_nobody_else_can()
    {
        var store = await Provision();
        await using (var pm = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager))
        {
            (await pm.GetGate.Handle(new GetPlanReviewGateQuery(), default)).Enabled.Should().BeFalse();
            await Thrown<ForbiddenException>(() => pm.SetGate.Handle(new SetPlanReviewGateCommand(true), default));
        }
        foreach (var role in AllRoles.Where(r => r != RoleNames.Admin))
        {
            await using var c = new Ctx(store, TestDb.TenantA, role);
            await Thrown<ForbiddenException>(() => c.SetGate.Handle(new SetPlanReviewGateCommand(true), default));
        }
        await using var admin = new Ctx(store, TestDb.TenantA, RoleNames.Admin);
        (await admin.SetGate.Handle(new SetPlanReviewGateCommand(true), default)).Enabled.Should().BeTrue();
        (await admin.GetGate.Handle(new GetPlanReviewGateQuery(), default)).Should().Be(new PlanReviewGateSettingDto(true, PlanReviewGate.StepKey));
        await using var otherTenant = new Ctx(store, TestDb.TenantB, RoleNames.Admin);
        (await otherTenant.GetGate.Handle(new GetPlanReviewGateQuery(), default)).Enabled.Should().BeFalse("the setting belongs to one agency");
    }

    [Fact]
    public async Task ROW_239_With_the_gate_on_a_parcel_stays_until_the_engineer_and_surveyor_approve()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA, step: PlanReviewGate.StepKey);
        await using var admin = new Ctx(store, TestDb.TenantA, RoleNames.Admin);
        await admin.SetGate.Handle(new SetPlanReviewGateCommand(true), default);
        await using var pm = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        var blocked = await Thrown<ConflictException>(() => pm.Advance.Handle(new AdvanceParcelStepCommand(parcel, "appraisal", null), default));
        blocked.Code.Should().Be("plan_review_required");
        blocked.Message.Should().Contain("engineer and surveyor");

        var status = await pm.AddReview.Handle(new AddPlanReviewCommand(parcel, ReviewDiscipline.Engineer, ReviewOutcome.Approved, Today, "Morgan Ellis, P.E.", null), default);
        status.Missing.Should().Equal("Surveyor");
        var still = await Thrown<ConflictException>(() => pm.Advance.Handle(new AdvanceParcelStepCommand(parcel, "appraisal", null), default));
        still.Message.Should().Contain("surveyor").And.NotContain("engineer");

        status = await pm.AddReview.Handle(new AddPlanReviewCommand(parcel, ReviewDiscipline.Surveyor, ReviewOutcome.Approved, Today, "Sam Ortiz, PLS", null), default);
        status.Satisfied.Should().BeTrue();
        (await pm.Advance.Handle(new AdvanceParcelStepCommand(parcel, "appraisal", null), default)).Workflow.CurrentKey.Should().Be("appraisal");
    }

    [Fact]
    public async Task ROW_239_With_the_gate_off_the_parcel_moves_without_any_review_and_the_override_cannot_beat_the_gate()
    {
        var store = await Provision();
        var (free, _) = await AddParcel(store, TestDb.TenantA, step: PlanReviewGate.StepKey);
        var (held, _) = await AddParcel(store, TestDb.TenantB, step: PlanReviewGate.StepKey);
        await using (var a = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager))
            (await a.Advance.Handle(new AdvanceParcelStepCommand(free, "appraisal", null), default)).Workflow.CurrentKey.Should().Be("appraisal");

        await using var admin = new Ctx(store, TestDb.TenantB, RoleNames.Admin);
        await admin.SetGate.Handle(new SetPlanReviewGateCommand(true), default);
        var blocked = await Thrown<ConflictException>(() => admin.Advance.Handle(new AdvanceParcelStepCommand(held, "offer", "Cleared under the 2024 project"), default));
        blocked.Code.Should().Be("plan_review_required");
        (await admin.Db.Parcels.FirstAsync(p => p.Id == held)).CurrentStepKey.Should().Be(PlanReviewGate.StepKey, "a blocked move changes nothing");
    }

    [Fact]
    public async Task ROW_239_A_later_review_that_asks_for_changes_holds_the_parcel_again_and_removing_it_releases_it()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA, step: PlanReviewGate.StepKey);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.Admin);
        await c.SetGate.Handle(new SetPlanReviewGateCommand(true), default);
        await c.AddReview.Handle(new AddPlanReviewCommand(parcel, ReviewDiscipline.Engineer, ReviewOutcome.Approved, Today.AddDays(-3), "Morgan", null), default);
        await c.AddReview.Handle(new AddPlanReviewCommand(parcel, ReviewDiscipline.Surveyor, ReviewOutcome.Approved, Today.AddDays(-3), "Sam", null), default);
        var withChanges = await c.AddReview.Handle(new AddPlanReviewCommand(parcel, ReviewDiscipline.Engineer, ReviewOutcome.ChangesRequired, Today, "Morgan", "Culvert moved"), default);
        withChanges.Missing.Should().Equal("Engineer");
        withChanges.Reviews.Should().HaveCount(3).And.BeInDescendingOrder(r => r.ReviewedOn);

        var afterRemoval = await c.RemoveReview.Handle(new RemovePlanReviewCommand(parcel, withChanges.Reviews.First().Id), default);
        afterRemoval.Satisfied.Should().BeTrue("the earlier approval counts again");
    }

    [Fact]
    public async Task ROW_239_A_review_needs_a_reviewer_a_non_future_date_and_notes_when_changes_are_required()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        var future = await Thrown<ValidationException>(() => c.AddReview.Handle(new AddPlanReviewCommand(parcel, ReviewDiscipline.Engineer, ReviewOutcome.Approved, Today.AddDays(1), "Morgan", null), default));
        future.Errors.Should().Contain(e => e.PropertyName == "ReviewedOn");
        (await c.Db.ParcelPlanReviews.CountAsync()).Should().Be(0);

        var validator = new AddPlanReviewCommandValidator();
        validator.Validate(new AddPlanReviewCommand(parcel, ReviewDiscipline.Engineer, ReviewOutcome.Approved, Today, "  ", null)).Errors.Should().Contain(e => e.PropertyName == "ReviewerName");
        validator.Validate(new AddPlanReviewCommand(parcel, ReviewDiscipline.Engineer, ReviewOutcome.ChangesRequired, Today, "Morgan", null)).Errors.Should().Contain(e => e.PropertyName == "Notes");
        validator.Validate(new AddPlanReviewCommand(parcel, (ReviewDiscipline)9, ReviewOutcome.Approved, Today, "Morgan", null)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ROW_239_Plan_reviews_are_recorded_by_the_parcel_roles_only()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        foreach (var role in AllRoles)
        {
            await using var c = new Ctx(store, TestDb.TenantA, role);
            (await c.Reviews.Handle(new GetParcelPlanReviewsQuery(parcel), default)).Should().NotBeNull(role);
            var write = () => c.AddReview.Handle(new AddPlanReviewCommand(parcel, ReviewDiscipline.Engineer, ReviewOutcome.Approved, Today, "Morgan", null), default);
            if (role is RoleNames.ProjectManager or RoleNames.Admin) await write.Should().NotThrowAsync(role);
            else await write.Should().ThrowAsync<ForbiddenException>(role);
        }
    }

    // ── staff reports and certificates (ROW-240) ──

    private static CreateStaffReportCommand Draft(Guid parcel, ReportKind kind = ReportKind.StaffReport) => new(parcel, kind, "  Staff report 001 ", " Needed for the approach. ");

    private static async Task<StaffReportDto> Submitted(Ctx author, Guid parcel, ReportKind kind = ReportKind.StaffReport)
    {
        var draft = await author.CreateReport.Handle(Draft(parcel, kind), default);
        return await author.SubmitReport.Handle(new SubmitStaffReportCommand(parcel, draft.Id), default);
    }

    [Fact]
    public async Task ROW_240_A_report_goes_from_draft_to_submitted_to_approved_by_someone_else()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using var pm = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager, PmId);
        await using var finance = new Ctx(store, TestDb.TenantA, RoleNames.Finance, FinanceId);

        var draft = await pm.CreateReport.Handle(Draft(parcel), default);
        draft.Status.Should().Be(ReportStatus.Draft);
        draft.Title.Should().Be("Staff report 001");
        var edited = await pm.UpdateReport.Handle(new UpdateStaffReportCommand(parcel, draft.Id, "Staff report 001 (revised)", "More detail."), default);
        edited.Body.Should().Be("More detail.");

        var submitted = await pm.SubmitReport.Handle(new SubmitStaffReportCommand(parcel, draft.Id), default);
        submitted.Status.Should().Be(ReportStatus.Submitted);
        submitted.SubmittedByMe.Should().BeTrue();
        submitted.CanEdit.Should().BeFalse();
        await Thrown<ConflictException>(() => pm.UpdateReport.Handle(new UpdateStaffReportCommand(parcel, draft.Id, "x", "y"), default));

        var approved = await finance.DecideReport.Handle(new DecideStaffReportCommand(parcel, draft.Id, true, null), default);
        approved.Status.Should().Be(ReportStatus.Approved);
        approved.SubmittedByMe.Should().BeFalse();
        await Thrown<ConflictException>(() => finance.DecideReport.Handle(new DecideStaffReportCommand(parcel, draft.Id, true, null), default));
        await Thrown<ConflictException>(() => pm.DeleteReport.Handle(new DeleteStaffReportCommand(parcel, draft.Id), default));
    }

    [Fact]
    public async Task ROW_240_The_submitter_cannot_approve_or_reject_their_own_report_even_with_the_approval_role()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using var admin = new Ctx(store, TestDb.TenantA, RoleNames.Admin, PmId);
        var report = await Submitted(admin, parcel);

        (await Thrown<ForbiddenException>(() => admin.DecideReport.Handle(new DecideStaffReportCommand(parcel, report.Id, true, null), default))).Message.Should().Contain("someone else");
        await Thrown<ForbiddenException>(() => admin.DecideReport.Handle(new DecideStaffReportCommand(parcel, report.Id, false, "no"), default));
        (await admin.Db.ParcelStaffReports.FirstAsync()).Status.Should().Be(ReportStatus.Submitted);
    }

    [Fact]
    public async Task ROW_240_A_rejected_report_needs_a_reason_can_be_reworked_and_resubmitted_and_a_new_one_may_replace_it()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using var pm = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager, PmId);
        await using var finance = new Ctx(store, TestDb.TenantA, RoleNames.Finance, FinanceId);
        var report = await Submitted(pm, parcel);

        new DecideStaffReportCommandValidator().Validate(new DecideStaffReportCommand(parcel, report.Id, false, " ")).Errors.Should().Contain(e => e.PropertyName == "Note");
        var rejected = await finance.DecideReport.Handle(new DecideStaffReportCommand(parcel, report.Id, false, "Needs the cost basis."), default);
        rejected.Status.Should().Be(ReportStatus.Rejected);
        rejected.DecisionNote.Should().Be("Needs the cost basis.");
        rejected.CanEdit.Should().BeTrue();
        pm.Db.ChangeTracker.Clear(); // the other context changed it

        // A rejected report does not block a fresh one of the same kind; both can then be seen.
        await pm.CreateReport.Handle(Draft(parcel), default);
        (await pm.Reports.Handle(new ListStaffReportsQuery(parcel), default)).Should().HaveCount(2);

        // The old one can be reworked and resubmitted only when no other live report of the kind exists, so delete the new draft first.
        await Thrown<ConflictException>(() => pm.CreateReport.Handle(Draft(parcel), default));
        await pm.DeleteReport.Handle(new DeleteStaffReportCommand(parcel, (await pm.Reports.Handle(new ListStaffReportsQuery(parcel), default)).First(r => r.Status == ReportStatus.Draft).Id), default);
        var resubmitted = await pm.SubmitReport.Handle(new SubmitStaffReportCommand(parcel, report.Id), default);
        resubmitted.Status.Should().Be(ReportStatus.Submitted);
        resubmitted.DecisionNote.Should().BeNull("a fresh submission clears the old decision");
    }

    [Fact]
    public async Task ROW_240_There_is_one_live_report_of_each_kind_per_parcel()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        var (other, _) = await AddParcel(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        await c.CreateReport.Handle(Draft(parcel), default);
        (await Thrown<ConflictException>(() => c.CreateReport.Handle(Draft(parcel), default))).Code.Should().Be("report_exists");
        await c.CreateReport.Handle(Draft(parcel, ReportKind.CertificateOfNecessity), default);
        await c.CreateReport.Handle(Draft(other), default);
    }

    [Fact]
    public async Task ROW_240_Reports_are_written_by_parcel_roles_and_decided_by_approval_roles_for_every_role()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using var author = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager, PmId);
        var submitted = await Submitted(author, parcel);

        foreach (var role in AllRoles)
        {
            var id = Guid.NewGuid();
            await using var c = new Ctx(store, TestDb.TenantA, role, id);
            (await c.Reports.Handle(new ListStaffReportsQuery(parcel), default)).Should().HaveCount(1, role);

            var create = () => c.CreateReport.Handle(Draft(parcel, ReportKind.CertificateOfNecessity), default);
            if (role is RoleNames.ProjectManager or RoleNames.Admin) { await create.Should().NotThrowAsync(role); c.Db.ParcelStaffReports.RemoveRange(await c.Db.ParcelStaffReports.Where(r => r.Kind == ReportKind.CertificateOfNecessity).ToListAsync()); await c.Db.SaveChangesAsync(); }
            else await create.Should().ThrowAsync<ForbiddenException>(role);

            var decide = () => c.DecideReport.Handle(new DecideStaffReportCommand(parcel, submitted.Id, false, "Try again."), default);
            // Reject (not approve) so the report can be used by the next role: put it back afterwards.
            if (role is RoleNames.ProjectManager or RoleNames.Appraiser or RoleNames.Finance or RoleNames.Admin)
            {
                await decide.Should().NotThrowAsync(role);
                var row = await c.Db.ParcelStaffReports.FirstAsync(r => r.Id == submitted.Id);
                row.Status = ReportStatus.Submitted;
                row.SubmittedBy = PmId;
                await c.Db.SaveChangesAsync();
            }
            else await decide.Should().ThrowAsync<ForbiddenException>(role);
        }
    }

    [Fact]
    public async Task ROW_240_A_report_belongs_to_its_parcel_and_its_tenant()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        var (other, _) = await AddParcel(store, TestDb.TenantA);
        await using var a = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var report = await a.CreateReport.Handle(Draft(parcel), default);

        await Thrown<NotFoundException>(() => a.UpdateReport.Handle(new UpdateStaffReportCommand(other, report.Id, "t", "b"), default));
        await using var b = new Ctx(store, TestDb.TenantB, RoleNames.Admin);
        await Thrown<NotFoundException>(() => b.Reports.Handle(new ListStaffReportsQuery(parcel), default));
    }

    // ── acquisition requests (ROW-080) ──

    private static SubmitAcquisitionRequestCommand Request(Guid? project = null, RequesterType type = RequesterType.Internal) =>
        new(project, type, "  Design team ", "design@example.com", " Lot behind 112 Harbor Rd ", "Easement for 18 months", 45_000m);

    [Fact]
    public async Task ROW_080_A_request_is_submitted_then_approved_by_a_different_person()
    {
        var store = await Provision();
        var (_, project) = await AddParcel(store, TestDb.TenantA);
        await using var pm = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager, PmId);
        await using var finance = new Ctx(store, TestDb.TenantA, RoleNames.Finance, FinanceId);

        var submitted = await pm.Submit.Handle(Request(project), default);
        submitted.Status.Should().Be(AcquisitionRequestStatus.Submitted);
        submitted.RequesterName.Should().Be("Design team");
        submitted.Property.Should().Be("Lot behind 112 Harbor Rd");
        submitted.SubmittedByMe.Should().BeTrue();
        submitted.ProjectNumber.Should().NotBeNull();

        await Thrown<ForbiddenException>(() => pm.Decide.Handle(new DecideAcquisitionRequestCommand(submitted.Id, true, null), default));
        var approved = await finance.Decide.Handle(new DecideAcquisitionRequestCommand(submitted.Id, true, "  Fund from FY27 budget "), default);
        approved.Status.Should().Be(AcquisitionRequestStatus.Approved);
        approved.DecisionNote.Should().Be("Fund from FY27 budget");
        approved.SubmittedByMe.Should().BeFalse();
        (await Thrown<ConflictException>(() => finance.Decide.Handle(new DecideAcquisitionRequestCommand(submitted.Id, false, "late"), default))).Code.Should().Be("request_decided");
        pm.Db.ChangeTracker.Clear(); // the other context changed it
        await Thrown<ConflictException>(() => pm.Withdraw.Handle(new WithdrawAcquisitionRequestCommand(submitted.Id), default));
    }

    [Fact]
    public async Task ROW_080_A_rejection_needs_a_reason_and_an_outside_requester_may_have_no_project()
    {
        var store = await Provision();
        await using var pm = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager, PmId);
        await using var finance = new Ctx(store, TestDb.TenantA, RoleNames.Finance, FinanceId);
        var request = await pm.Submit.Handle(Request(type: RequesterType.External), default);
        request.ProjectId.Should().BeNull();

        new DecideAcquisitionRequestCommandValidator().Validate(new DecideAcquisitionRequestCommand(request.Id, false, null)).Errors.Should().Contain(e => e.PropertyName == "Note");
        (await finance.Decide.Handle(new DecideAcquisitionRequestCommand(request.Id, false, "Outside the corridor."), default)).Status.Should().Be(AcquisitionRequestStatus.Rejected);
    }

    [Fact]
    public async Task ROW_080_Only_the_submitter_withdraws_and_only_while_it_is_waiting()
    {
        var store = await Provision();
        await using var pm = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager, PmId);
        await using var admin = new Ctx(store, TestDb.TenantA, RoleNames.Admin, FinanceId);
        var request = await pm.Submit.Handle(Request(), default);

        await Thrown<ForbiddenException>(() => admin.Withdraw.Handle(new WithdrawAcquisitionRequestCommand(request.Id), default));
        (await pm.Withdraw.Handle(new WithdrawAcquisitionRequestCommand(request.Id), default)).Status.Should().Be(AcquisitionRequestStatus.Withdrawn);
        await Thrown<ConflictException>(() => pm.Withdraw.Handle(new WithdrawAcquisitionRequestCommand(request.Id), default));
        admin.Db.ChangeTracker.Clear(); // the other context changed it
        await Thrown<ConflictException>(() => admin.Decide.Handle(new DecideAcquisitionRequestCommand(request.Id, true, null), default));
    }

    [Fact]
    public async Task ROW_080_Request_details_are_validated_and_an_unknown_project_is_refused()
    {
        var validator = new SubmitAcquisitionRequestCommandValidator();
        validator.Validate(Request()).IsValid.Should().BeTrue();
        validator.Validate(Request() with { RequesterName = " " }).Errors.Should().Contain(e => e.PropertyName == "RequesterName");
        validator.Validate(Request() with { Property = "" }).Errors.Should().Contain(e => e.PropertyName == "Property");
        validator.Validate(Request() with { RequesterEmail = "not an email" }).Errors.Should().Contain(e => e.PropertyName == "RequesterEmail");
        validator.Validate(Request() with { EstimatedAmount = -1m }).Errors.Should().Contain(e => e.PropertyName == "EstimatedAmount");
        validator.Validate(Request() with { RequesterType = (RequesterType)9 }).IsValid.Should().BeFalse();

        var store = await Provision();
        await using var pm = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        await Thrown<NotFoundException>(() => pm.Submit.Handle(Request(Guid.NewGuid()), default));
    }

    [Fact]
    public async Task ROW_080_Requests_are_submitted_by_project_roles_read_and_decided_by_approval_roles_for_every_role()
    {
        var store = await Provision();
        await using (var seed = new Ctx(store, TestDb.TenantA, RoleNames.Admin, FinanceId)) await seed.Submit.Handle(Request(), default);

        foreach (var role in AllRoles)
        {
            await using var c = new Ctx(store, TestDb.TenantA, role, Guid.NewGuid());
            var list = () => c.Requests.Handle(new ListAcquisitionRequestsQuery(null), default);
            var submit = () => c.Submit.Handle(Request(), default);
            if (role == RoleNames.TitleResearcher) await list.Should().ThrowAsync<ForbiddenException>(role); else await list.Should().NotThrowAsync(role);
            if (role is RoleNames.ProjectManager or RoleNames.Admin) await submit.Should().NotThrowAsync(role); else await submit.Should().ThrowAsync<ForbiddenException>(role);
        }
    }

    [Fact]
    public async Task ROW_080_The_inbox_is_filtered_newest_first_and_private_to_the_tenant()
    {
        var store = await Provision();
        await using var pm = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager, PmId);
        await using var finance = new Ctx(store, TestDb.TenantA, RoleNames.Finance, FinanceId);
        var first = await pm.Submit.Handle(Request(), default);
        await pm.Submit.Handle(Request(), default);
        await finance.Decide.Handle(new DecideAcquisitionRequestCommand(first.Id, true, null), default);

        (await finance.Requests.Handle(new ListAcquisitionRequestsQuery(null), default)).Total.Should().Be(2);
        (await finance.Requests.Handle(new ListAcquisitionRequestsQuery("Submitted"), default)).Items.Should().ContainSingle();
        (await finance.Requests.Handle(new ListAcquisitionRequestsQuery("approved"), default)).Items.Should().ContainSingle().Which.Id.Should().Be(first.Id);
        new ListAcquisitionRequestsQueryValidator().Validate(new ListAcquisitionRequestsQuery("Bogus")).IsValid.Should().BeFalse();

        await using var other = new Ctx(store, TestDb.TenantB, RoleNames.Admin);
        (await other.Requests.Handle(new ListAcquisitionRequestsQuery(null), default)).Total.Should().Be(0);
        await Thrown<NotFoundException>(() => other.Decide.Handle(new DecideAcquisitionRequestCommand(first.Id, true, null), default));
    }

    // ── acquisition plan (ROW-241) ──

    [Fact]
    public async Task ROW_241_The_plan_is_computed_from_the_live_parcels_budget_estimate_and_authorizations()
    {
        var store = await Provision();
        var (parcel, project) = await AddParcel(store, TestDb.TenantA);
        await AddParcel(store, TestDb.TenantA); // a different project: must not appear
        await using (var db = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            db.ParcelMilestones.AddRange(
                new ParcelMilestone { ParcelId = parcel, Name = MilestoneNames.NoticeToOwner, DueOn = Today.AddDays(-2) },
                new ParcelMilestone { ParcelId = parcel, Name = MilestoneNames.Closed, DueOn = Today.AddDays(100) });
            db.ProjectBudgets.AddRange(new ProjectBudget { ProjectId = project, FiscalYear = 2026, Amount = 600m }, new ProjectBudget { ProjectId = project, FiscalYear = 2027, Amount = 400m });
            db.ProjectCostEstimates.AddRange(new ProjectCostEstimate { ProjectId = project, Version = 1, Total = 800m }, new ProjectCostEstimate { ProjectId = project, Version = 2, Total = 1_100m });
            db.RowAuthorizations.Add(new RowAuthorization { ProjectId = project, Type = AuthorizationType.Acquisition, AuthorizedAmount = 700m, AuthorizedOn = Today });
            await db.SaveChangesAsync();
        }

        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.Finance);
        var plan = (await c.Plan.Handle(new GetAcquisitionPlanQuery(project), default)).Plan;

        plan.ParcelCount.Should().Be(1);
        plan.OverdueCount.Should().Be(1);
        plan.LastPlannedCloseOn.Should().Be(Today.AddDays(100));
        plan.Parcels.Single().NextMilestone.Should().Be(MilestoneNames.NoticeToOwner);
        plan.Budget.TotalBudget.Should().Be(1_000m);
        plan.Budget.CurrentEstimate.Should().Be(1_100m, "the latest version counts");
        plan.Budget.Variance.Should().Be(-100m);
        plan.Budget.OverBudget.Should().BeTrue();
        plan.Budget.AuthorizedTotal.Should().Be(700m);
    }

    [Fact]
    public async Task ROW_241_The_plan_leaves_out_superseded_parcels_and_is_private_to_the_tenant()
    {
        var store = await Provision();
        var (_, project) = await AddParcel(store, TestDb.TenantA);
        await using (var db = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            var status = await db.ParcelStatuses.FirstAsync();
            db.Parcels.Add(new Parcel { ProjectId = project, Number = "002", StatusId = status.Id, CurrentStepKey = "setup", AreaSqFt = 5, State = ParcelState.Split });
            await db.SaveChangesAsync();
        }

        await using var a = new Ctx(store, TestDb.TenantA, RoleNames.Appraiser);
        (await a.Plan.Handle(new GetAcquisitionPlanQuery(project), default)).Plan.ParcelCount.Should().Be(1);
        await using var b = new Ctx(store, TestDb.TenantB, RoleNames.Admin);
        await Thrown<NotFoundException>(() => b.Plan.Handle(new GetAcquisitionPlanQuery(project), default));
        await Thrown<NotFoundException>(() => a.Plan.Handle(new GetAcquisitionPlanQuery(Guid.NewGuid()), default));
    }

    [Fact]
    public async Task ROW_241_Every_role_that_can_read_parcels_can_read_the_plan()
    {
        var store = await Provision();
        var (_, project) = await AddParcel(store, TestDb.TenantA);
        foreach (var role in AllRoles)
        {
            await using var c = new Ctx(store, TestDb.TenantA, role);
            (await c.Plan.Handle(new GetAcquisitionPlanQuery(project), default)).Plan.ParcelCount.Should().Be(1, role);
        }
    }
}
