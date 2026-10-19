using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurigo.RowManagement.Application.AcquisitionPlans;
using Aurigo.RowManagement.Application.AcquisitionRequests;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.PlanReviews;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.Application.RightsOfEntry;
using Aurigo.RowManagement.Application.StaffReports;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>
/// Rights of entry, acquisition requests, the plan-review gate, staff reports and the acquisition plan on the real API and Postgres
/// (ROW-077, 080, 239, 240, 241). Matches AT-PM-30..34, AT-FN-24..25, AT-AP-25, AT-NG-26, AT-TE-25, AT-AD-29, AT-X-40.
/// </summary>
public class AcquisitionApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly Guid OtherTenant = Guid.Parse("00000000-0000-0000-0000-0000000000a8");
    private static readonly string[] AllRoles = { "ProjectManager", "TitleResearcher", "Appraiser", "Negotiator", "Finance", "Admin" };
    private readonly PgApi _api;
    public AcquisitionApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> t) => (await t).StatusCode;
    private static string Today(int addDays = 0) => DateTime.UtcNow.AddDays(addDays).ToString("yyyy-MM-dd");

    private async Task<ProjectDto> NewProject(TestUser user) =>
        await Ok<ProjectDto>(await user.Http.PostAsJsonAsync("/api/v1/projects", new { name = $"Acq {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local" }, Json), HttpStatusCode.Created);

    private async Task<ParcelDto> NewParcel(TestUser user, Guid? projectId = null)
    {
        var project = projectId ?? (await NewProject(user)).Id;
        return await Ok<ParcelDto>(await user.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project, areaSqFt = 100m, acquisitionType = "Standard" }, Json), HttpStatusCode.Created);
    }

    private static object Roe(string status = "Granted", int? requested = -20, int? granted = -10, int? ends = 30, string by = "Alex Marlowe") =>
        new { status, requestedOn = requested is { } r ? Today(r) : null, grantedOn = granted is { } g ? Today(g) : null, expiresOn = ends is { } e ? Today(e) : null, grantedBy = by, purpose = "Survey", notes = (string?)null };

    private static object Req(Guid? project = null, string type = "Internal") =>
        new { projectId = project, requesterType = type, requesterName = "Design team", requesterEmail = "design@example.com", property = "Lot behind 112 Harbor Rd", desiredParameters = "Easement", estimatedAmount = 45_000m };

    private async Task EnsureOtherTenantAsync()
    {
        using var scope = _api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();
        if (!await db.Tenants.AnyAsync(t => t.Id == OtherTenant))
        {
            db.Tenants.Add(new Tenant { Id = OtherTenant, Name = "Other Agency A8", Slug = "other-a8", IsActive = true });
            await db.SaveChangesAsync();
        }
        await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().EnsureDefaultRolesAsync(OtherTenant);
    }

    // ── the jobs ──

    [PgFact]
    public async Task AT_PM_30_A_project_manager_records_changes_and_removes_a_right_of_entry_and_it_appears_in_the_report()
    {
        var pm = await _api.As("ProjectManager");
        var parcel = await NewParcel(pm);

        var granted = await Ok<RightOfEntryDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry", Roe(by: "  Alex Marlowe "), Json), HttpStatusCode.Created);
        granted.EffectiveStatus.Should().Be("Granted");
        granted.GrantedBy.Should().Be("Alex Marlowe");

        var noDate = await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry", Roe(granted: null), Json);
        noDate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noDate.Content.ReadAsStringAsync()).Should().Contain("Enter the date permission was given");
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry", Roe(granted: 3), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "granted in the future");
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry", Roe(granted: -10, ends: -20), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "ends before it was given");

        var ended = await Ok<RightOfEntryDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry", Roe(requested: -60, granted: -50, ends: -1), Json), HttpStatusCode.Created);
        ended.EffectiveStatus.Should().Be("Expired");

        var revoked = await Ok<RightOfEntryDto>(await pm.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry/{granted.Id}", Roe("Revoked"), Json));
        revoked.EffectiveStatus.Should().Be("Revoked");

        var report = await Ok<PagedResult<RightOfEntryReportRowDto>>(await pm.Http.GetAsync($"/api/v1/rights-of-entry?projectId={parcel.ProjectId}"));
        report.Items.Select(r => r.EffectiveStatus).Should().BeEquivalentTo("Revoked", "Expired");
        report.Items.Should().OnlyContain(r => r.ParcelNumber == parcel.Number && r.ProjectId == parcel.ProjectId);

        (await pm.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry/{revoked.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await pm.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry/{revoked.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_type = 'RightOfEntry' and entity_id = @id and action = 'Delete'", ("id", revoked.Id))).Should().Be(1);
    }

    [PgFact]
    public async Task AT_X_40_Anyone_who_can_see_parcels_filters_the_report_of_rights_of_entry_by_status_and_expiry()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var one = await NewParcel(pm, project.Id);
        var two = await NewParcel(pm, project.Id);
        await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{one.Id}/rights-of-entry", Roe(ends: 10), Json);
        await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{one.Id}/rights-of-entry", Roe(ends: 200), Json);
        await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{two.Id}/rights-of-entry", Roe(requested: -60, granted: -50, ends: -2), Json);
        await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{two.Id}/rights-of-entry", Roe("Requested", requested: -1, granted: null, ends: null), Json);

        foreach (var role in AllRoles)
        {
            var user = await _api.As(role);
            var all = await Ok<PagedResult<RightOfEntryReportRowDto>>(await user.Http.GetAsync($"/api/v1/rights-of-entry?projectId={project.Id}"));
            all.Total.Should().Be(4, role);

            (await Ok<PagedResult<RightOfEntryReportRowDto>>(await user.Http.GetAsync($"/api/v1/rights-of-entry?projectId={project.Id}&status=Expired"))).Items.Should().ContainSingle(role);
            (await Ok<PagedResult<RightOfEntryReportRowDto>>(await user.Http.GetAsync($"/api/v1/rights-of-entry?projectId={project.Id}&status=Granted"))).Total.Should().Be(2, role);
            (await Ok<PagedResult<RightOfEntryReportRowDto>>(await user.Http.GetAsync($"/api/v1/rights-of-entry?projectId={project.Id}&status=Requested"))).Total.Should().Be(1, role);
            var soon = await Ok<PagedResult<RightOfEntryReportRowDto>>(await user.Http.GetAsync($"/api/v1/rights-of-entry?projectId={project.Id}&expiringWithinDays=30"));
            soon.Items.Should().ContainSingle(role).Which.DaysUntilExpiry.Should().Be(10);
        }

        var anyone = await _api.As("Negotiator");
        foreach (var bad in new[] { "status=Bogus", "expiringWithinDays=-1", "expiringWithinDays=9999", "page=0", "pageSize=0", "pageSize=500", "projectId=not-a-guid" })
            (await anyone.Http.GetAsync($"/api/v1/rights-of-entry?{bad}")).StatusCode.Should().Be(HttpStatusCode.BadRequest, bad);
    }

    [PgFact]
    public async Task AT_PM_31_A_project_manager_submits_a_request_and_someone_else_approves_it()
    {
        var pm = await _api.As("ProjectManager");
        var finance = await _api.As("Finance");
        var project = await NewProject(pm);

        var request = await Ok<AcquisitionRequestDto>(await pm.Http.PostAsJsonAsync("/api/v1/acquisition-requests", Req(project.Id), Json), HttpStatusCode.Created);
        request.Status.ToString().Should().Be("Submitted");
        request.SubmittedByMe.Should().BeTrue();
        request.ProjectNumber.Should().Be(project.Number);

        (await pm.Http.PostAsJsonAsync($"/api/v1/acquisition-requests/{request.Id}/approve", new { note = "me" }, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "nobody decides their own request");
        var reject = await finance.Http.PostAsJsonAsync($"/api/v1/acquisition-requests/{request.Id}/reject", new { note = " " }, Json);
        reject.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await reject.Content.ReadAsStringAsync()).Should().Contain("Say why the request is rejected");

        var approved = await Ok<AcquisitionRequestDto>(await finance.Http.PostAsJsonAsync($"/api/v1/acquisition-requests/{request.Id}/approve", new { note = "Fund from FY27" }, Json));
        approved.Status.ToString().Should().Be("Approved");
        approved.SubmittedByMe.Should().BeFalse();
        (await finance.Http.PostAsJsonAsync($"/api/v1/acquisition-requests/{request.Id}/reject", new { note = "too late" }, Json)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await pm.Http.PostAsync($"/api/v1/acquisition-requests/{request.Id}/withdraw", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var listed = await Ok<PagedResult<AcquisitionRequestDto>>(await finance.Http.GetAsync("/api/v1/acquisition-requests?status=approved"));
        listed.Items.Should().Contain(r => r.Id == request.Id);
        (await Ok<AcquisitionRequestDto>(await finance.Http.GetAsync($"/api/v1/acquisition-requests/{request.Id}"))).Id.Should().Be(request.Id);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from acquisition_requests where id = @id and status = 'Approved' and decided_by = @u and decision_note = 'Fund from FY27'", ("id", request.Id), ("u", finance.UserId))).Should().Be(1);
    }

    [PgFact]
    public async Task AT_PM_31_The_same_person_with_the_approval_role_cannot_decide_a_request_they_entered_but_the_submitter_can_withdraw_it()
    {
        var me = Guid.NewGuid();
        var admin = await _api.As("Admin", user: me);
        var otherAdmin = await _api.As("Admin");
        var request = await Ok<AcquisitionRequestDto>(await admin.Http.PostAsJsonAsync("/api/v1/acquisition-requests", Req(type: "External"), Json), HttpStatusCode.Created);
        request.ProjectId.Should().BeNull();

        (await admin.Http.PostAsJsonAsync($"/api/v1/acquisition-requests/{request.Id}/approve", new { }, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await otherAdmin.Http.PostAsync($"/api/v1/acquisition-requests/{request.Id}/withdraw", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "only the person who entered it can withdraw it");
        (await Ok<AcquisitionRequestDto>(await admin.Http.PostAsync($"/api/v1/acquisition-requests/{request.Id}/withdraw", null))).Status.ToString().Should().Be("Withdrawn");
        (await otherAdmin.Http.PostAsJsonAsync($"/api/v1/acquisition-requests/{request.Id}/approve", new { }, Json)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [PgFact]
    public async Task AT_PM_32_Turning_the_plan_review_gate_on_holds_a_parcel_until_the_engineer_and_surveyor_approve()
    {
        var admin = await _api.As("Admin");
        var pm = await _api.As("ProjectManager");
        var parcel = await NewParcel(pm);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = "title_survey" }, Json)).StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            (await Ok<PlanReviewGateSettingDto>(await admin.Http.PutAsJsonAsync("/api/v1/agency/plan-review-gate", new { enabled = true }, Json))).Enabled.Should().BeTrue();

            var held = await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = "appraisal" }, Json);
            held.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await held.Content.ReadAsStringAsync()).Should().Contain("plan_review_required").And.Contain("engineer and surveyor");
            var overridden = await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = "offer", overrideReason = "Cleared by the director" }, Json);
            overridden.StatusCode.Should().Be(HttpStatusCode.Conflict, "an override cannot beat the gate");

            var status = await Ok<PlanReviewStatusDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/plan-reviews",
                new { discipline = "Engineer", outcome = "Approved", reviewedOn = Today(), reviewerName = "Morgan Ellis, P.E." }, Json), HttpStatusCode.Created);
            status.Missing.Should().Equal("Surveyor");
            (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = "appraisal" }, Json)).StatusCode.Should().Be(HttpStatusCode.Conflict);

            var changes = await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/plan-reviews", new { discipline = "Surveyor", outcome = "ChangesRequired", reviewedOn = Today(), reviewerName = "Sam Ortiz" }, Json);
            changes.StatusCode.Should().Be(HttpStatusCode.BadRequest, "notes are needed when changes are required");
            status = await Ok<PlanReviewStatusDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/plan-reviews",
                new { discipline = "Surveyor", outcome = "Approved", reviewedOn = Today(), reviewerName = "Sam Ortiz, PLS" }, Json), HttpStatusCode.Created);
            status.Satisfied.Should().BeTrue();
            (await Ok<PlanReviewStatusDto>(await pm.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/plan-reviews"))).Reviews.Should().HaveCount(2);

            (await Ok<ParcelDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = "appraisal" }, Json))).Workflow.CurrentKey.Should().Be("appraisal");
        }
        finally
        {
            await admin.Http.PutAsJsonAsync("/api/v1/agency/plan-review-gate", new { enabled = false }, Json);
        }
    }

    [PgFact]
    public async Task AT_PM_32_With_the_gate_off_nothing_changes_and_a_future_review_date_is_refused()
    {
        var pm = await _api.As("ProjectManager");
        var parcel = await NewParcel(pm);
        (await Ok<PlanReviewGateSettingDto>(await pm.Http.GetAsync("/api/v1/agency/plan-review-gate"))).Enabled.Should().BeFalse();
        await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = "title_survey" }, Json);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = "appraisal" }, Json)).StatusCode.Should().Be(HttpStatusCode.OK, "the gate is off");

        var future = await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/plan-reviews", new { discipline = "Engineer", outcome = "Approved", reviewedOn = Today(2), reviewerName = "Morgan" }, Json);
        future.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_plan_reviews where parcel_id = @p", ("p", parcel.Id))).Should().Be(0);
    }

    [PgFact]
    public async Task AT_PM_33_A_project_manager_drafts_submits_and_reworks_a_staff_report_that_another_person_decides()
    {
        var pm = await _api.As("ProjectManager");
        var finance = await _api.As("Finance");
        var parcel = await NewParcel(pm);
        var url = $"/api/v1/parcels/{parcel.Id}/staff-reports";

        var blank = await pm.Http.PostAsJsonAsync(url, new { kind = "StaffReport", title = " ", body = "" }, Json);
        blank.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var draft = await Ok<StaffReportDto>(await pm.Http.PostAsJsonAsync(url, new { kind = "StaffReport", title = "  Staff report 001 ", body = "Needed for the approach." }, Json), HttpStatusCode.Created);
        draft.Title.Should().Be("Staff report 001");
        (await pm.Http.PostAsJsonAsync(url, new { kind = "StaffReport", title = "Second", body = "x" }, Json)).StatusCode.Should().Be(HttpStatusCode.Conflict, "one live report of a kind per parcel");
        await Ok<StaffReportDto>(await pm.Http.PostAsJsonAsync(url, new { kind = "CertificateOfNecessity", title = "Certificate", body = "It is necessary." }, Json), HttpStatusCode.Created);

        (await Ok<StaffReportDto>(await pm.Http.PutAsJsonAsync($"{url}/{draft.Id}", new { title = "Staff report 001 (v2)", body = "More detail." }, Json))).Body.Should().Be("More detail.");
        var submitted = await Ok<StaffReportDto>(await pm.Http.PostAsync($"{url}/{draft.Id}/submit", null));
        submitted.Status.ToString().Should().Be("Submitted");
        (await pm.Http.PutAsJsonAsync($"{url}/{draft.Id}", new { title = "x", body = "y" }, Json)).StatusCode.Should().Be(HttpStatusCode.Conflict, "a submitted report is locked");
        (await pm.Http.DeleteAsync($"{url}/{draft.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await pm.Http.PostAsJsonAsync($"{url}/{draft.Id}/approve", new { }, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "not your own submission");

        var rejected = await Ok<StaffReportDto>(await finance.Http.PostAsJsonAsync($"{url}/{draft.Id}/reject", new { note = "Needs the cost basis." }, Json));
        rejected.Status.ToString().Should().Be("Rejected");
        rejected.DecisionNote.Should().Be("Needs the cost basis.");
        await Ok<StaffReportDto>(await pm.Http.PutAsJsonAsync($"{url}/{draft.Id}", new { title = "Staff report 001 (v3)", body = "With the cost basis." }, Json));
        (await Ok<StaffReportDto>(await pm.Http.PostAsync($"{url}/{draft.Id}/submit", null))).DecisionNote.Should().BeNull();
        (await Ok<StaffReportDto>(await finance.Http.PostAsJsonAsync($"{url}/{draft.Id}/approve", new { }, Json))).Status.ToString().Should().Be("Approved");
        (await pm.Http.PutAsJsonAsync($"{url}/{draft.Id}", new { title = "x", body = "y" }, Json)).StatusCode.Should().Be(HttpStatusCode.Conflict, "approved reports are final");

        var list = await Ok<List<StaffReportDto>>(await pm.Http.GetAsync(url));
        list.Select(r => r.Kind.ToString()).Should().Equal("StaffReport", "CertificateOfNecessity");
    }

    [PgFact]
    public async Task AT_PM_34_The_project_plan_lists_parcels_milestones_and_the_budget_against_the_current_estimate()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var parcel = await NewParcel(pm, project.Id);
        await NewParcel(pm, project.Id);
        var empty = await Ok<AcquisitionPlanDto>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}/acquisition-plan"));
        empty.Plan.ParcelCount.Should().Be(2);
        empty.Plan.Budget.Variance.Should().BeNull("no budget or estimate yet");

        (await pm.Http.PutAsJsonAsync($"/api/v1/projects/{project.Id}/budgets/2026", new { amount = 1_000_000m }, Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await pm.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/cost-estimates", new { landCost = 1_200_000m, improvementsCost = 0m, relocationCost = 0m, indirectCost = 0m }, Json)).StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var plan = await Ok<AcquisitionPlanDto>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}/acquisition-plan"));

        plan.ProjectNumber.Should().Be(project.Number);
        plan.Plan.Parcels.Select(p => p.Number).Should().Equal("001", "002");
        plan.Plan.Budget.TotalBudget.Should().Be(1_000_000m);
        plan.Plan.Budget.CurrentEstimate.Should().Be(1_200_000m);
        plan.Plan.Budget.Variance.Should().Be(-200_000m);
        plan.Plan.Budget.OverBudget.Should().BeTrue();

        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/split", new { parts = new[] { new { areaSqFt = 40m } } }, Json)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await Ok<AcquisitionPlanDto>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}/acquisition-plan"))).Plan.ParcelCount.Should().Be(3, "the split parcel is replaced by its pieces: 1 + 2 live");
        (await pm.Http.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/acquisition-plan")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PgFact]
    public async Task AT_AD_29_An_administrator_turns_the_plan_review_gate_on_and_off_and_nobody_else_can()
    {
        var admin = await _api.As("Admin");
        try
        {
            (await Ok<PlanReviewGateSettingDto>(await admin.Http.PutAsJsonAsync("/api/v1/agency/plan-review-gate", new { enabled = true }, Json))).Enabled.Should().BeTrue();
            foreach (var role in AllRoles)
            {
                var user = await _api.As(role);
                (await Ok<PlanReviewGateSettingDto>(await user.Http.GetAsync("/api/v1/agency/plan-review-gate"))).Enabled.Should().BeTrue(role);
                if (role != "Admin") (await user.Http.PutAsJsonAsync("/api/v1/agency/plan-review-gate", new { enabled = false }, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden, role);
            }
            (await Ok<PlanReviewGateSettingDto>(await admin.Http.GetAsync("/api/v1/agency/plan-review-gate"))).Enabled.Should().BeTrue("a refused request changed nothing");
            (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_type = 'AgencySettings' and field = 'PlanReviewGateEnabled' and changed_by = @u", ("u", admin.UserId))).Should().BeGreaterThan(0);
        }
        finally
        {
            await admin.Http.PutAsJsonAsync("/api/v1/agency/plan-review-gate", new { enabled = false }, Json);
        }
    }

    // ── every role, both directions ──

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task Each_role_can_do_exactly_what_the_parcels_approvals_and_projects_pages_allow(string role)
    {
        var admin = await _api.As("Admin");
        var author = await _api.As("ProjectManager");
        var parcel = await NewParcel(admin);
        var seedRoe = await Ok<RightOfEntryDto>(await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry", Roe(), Json), HttpStatusCode.Created);
        var seedReview = (await Ok<PlanReviewStatusDto>(await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/plan-reviews",
            new { discipline = "Surveyor", outcome = "Approved", reviewedOn = Today(), reviewerName = "Seed" }, Json), HttpStatusCode.Created)).Reviews.Single();
        var draft = await Ok<StaffReportDto>(await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/staff-reports", new { kind = "StaffReport", title = "Seed", body = "Seed" }, Json), HttpStatusCode.Created);
        await Ok<StaffReportDto>(await author.Http.PostAsync($"/api/v1/parcels/{parcel.Id}/staff-reports/{draft.Id}/submit", null));
        var request = await Ok<AcquisitionRequestDto>(await admin.Http.PostAsJsonAsync("/api/v1/acquisition-requests", Req(parcel.ProjectId), Json), HttpStatusCode.Created);
        var user = await _api.As(role);
        bool Can(string page, string action) => Spec.Can(role, page, action);
        HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;

        // reading
        foreach (var path in new[] { "rights-of-entry", "plan-reviews", "staff-reports" })
            (await Status(user.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/{path}"))).Should().Be(Expect(Can("parcels", "read"), HttpStatusCode.OK), path);
        (await Status(user.Http.GetAsync("/api/v1/rights-of-entry"))).Should().Be(Expect(Can("parcels", "read"), HttpStatusCode.OK));
        (await Status(user.Http.GetAsync($"/api/v1/projects/{parcel.ProjectId}/acquisition-plan"))).Should().Be(Expect(Can("parcels", "read"), HttpStatusCode.OK));
        (await Status(user.Http.GetAsync("/api/v1/agency/plan-review-gate"))).Should().Be(Expect(Can("parcels", "read"), HttpStatusCode.OK));
        (await Status(user.Http.GetAsync("/api/v1/acquisition-requests"))).Should().Be(Expect(Can("approvals", "read"), HttpStatusCode.OK));
        (await Status(user.Http.GetAsync($"/api/v1/acquisition-requests/{request.Id}"))).Should().Be(Expect(Can("approvals", "read"), HttpStatusCode.OK));

        // writing on the parcel
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry", Roe(by: $"By {role}"), Json))).Should().Be(Expect(Can("parcels", "create"), HttpStatusCode.Created));
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry/{seedRoe.Id}", Roe(by: $"Edited by {role}"), Json))).Should().Be(Expect(Can("parcels", "update"), HttpStatusCode.OK));
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/plan-reviews", new { discipline = "Engineer", outcome = "Approved", reviewedOn = Today(), reviewerName = $"By {role}" }, Json))).Should().Be(Expect(Can("parcels", "update"), HttpStatusCode.Created));
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/staff-reports", new { kind = "CertificateOfNecessity", title = $"By {role}", body = "x" }, Json))).Should().Be(Expect(Can("parcels", "create"), HttpStatusCode.Created));

        // approvals
        var decidedReport = await Status(user.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/staff-reports/{draft.Id}/reject", new { note = $"By {role}" }, Json));
        decidedReport.Should().Be(Can("approvals", "update") ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/acquisition-requests/{request.Id}/reject", new { note = $"By {role}" }, Json))).Should().Be(Expect(Can("approvals", "update"), HttpStatusCode.OK));

        // submitting a request
        (await Status(user.Http.PostAsJsonAsync("/api/v1/acquisition-requests", Req(parcel.ProjectId), Json))).Should().Be(Expect(Can("projects", "create"), HttpStatusCode.Created));

        // deleting
        (await Status(user.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/plan-reviews/{seedReview.Id}"))).Should().Be(Expect(Can("parcels", "delete"), HttpStatusCode.OK));
        (await Status(user.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry/{seedRoe.Id}"))).Should().Be(Expect(Can("parcels", "delete"), HttpStatusCode.NoContent));

        if (!Can("parcels", "create"))
            (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from rights_of_entry where granted_by = @g", ("g", $"By {role}"))).Should().Be(0, "a refused request changes nothing");
    }

    public static IEnumerable<object[]> Roles() => AllRoles.Select(r => new object[] { r });

    [PgFact]
    public async Task Without_signing_in_none_of_it_is_available()
    {
        var anon = _api.Factory.CreateClient();
        foreach (var url in new[]
        {
            $"/api/v1/parcels/{Guid.NewGuid()}/rights-of-entry", $"/api/v1/parcels/{Guid.NewGuid()}/plan-reviews", $"/api/v1/parcels/{Guid.NewGuid()}/staff-reports",
            "/api/v1/rights-of-entry", "/api/v1/acquisition-requests", $"/api/v1/projects/{Guid.NewGuid()}/acquisition-plan", "/api/v1/agency/plan-review-gate",
        })
            (await anon.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
        (await anon.PostAsJsonAsync("/api/v1/acquisition-requests", Req(), Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── races, tenancy, schema, input ──

    [PgFact]
    public async Task Ten_people_creating_the_same_kind_of_report_at_once_produce_exactly_one()
    {
        var pm = await _api.As("ProjectManager");
        var parcel = await NewParcel(pm);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/staff-reports", new { kind = "StaffReport", title = $"Race {i}", body = "x" }, Json)));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Where(r => r.StatusCode != HttpStatusCode.Created).Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Conflict);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_staff_reports where parcel_id = @p", ("p", parcel.Id))).Should().Be(1);
    }

    [PgFact]
    public async Task Two_approvers_deciding_the_same_request_at_once_cannot_both_win()
    {
        var pm = await _api.As("ProjectManager");
        var request = await Ok<AcquisitionRequestDto>(await pm.Http.PostAsJsonAsync("/api/v1/acquisition-requests", Req(), Json), HttpStatusCode.Created);
        var approvers = new[] { await _api.As("Finance"), await _api.As("Admin"), await _api.As("Appraiser"), await _api.As("Finance") };

        var results = await Task.WhenAll(approvers.Select((u, i) => u.Http.PostAsJsonAsync($"/api/v1/acquisition-requests/{request.Id}/{(i % 2 == 0 ? "approve" : "reject")}", new { note = "race" }, Json)));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "the first decision stands; the rest are told it is already decided or changed");
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from acquisition_requests where id = @id and status in ('Approved','Rejected') and decided_by is not null", ("id", request.Id))).Should().Be(1);
    }

    [PgFact]
    public async Task One_agency_never_sees_or_changes_another_agencys_acquisition_records()
    {
        await EnsureOtherTenantAsync();
        var mine = await _api.As("Admin");
        var theirs = await _api.As("Admin", OtherTenant);
        var parcel = await NewParcel(mine);
        var roe = await Ok<RightOfEntryDto>(await mine.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry", Roe(), Json), HttpStatusCode.Created);
        var report = await Ok<StaffReportDto>(await mine.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/staff-reports", new { kind = "StaffReport", title = "Secret", body = "Secret" }, Json), HttpStatusCode.Created);
        var request = await Ok<AcquisitionRequestDto>(await mine.Http.PostAsJsonAsync("/api/v1/acquisition-requests", Req(parcel.ProjectId), Json), HttpStatusCode.Created);

        foreach (var part in new[] { "rights-of-entry", "plan-reviews", "staff-reports" })
            (await theirs.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/{part}")).StatusCode.Should().Be(HttpStatusCode.NotFound, part);
        (await theirs.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry", Roe(by: "Intruder"), Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry/{roe.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PostAsync($"/api/v1/parcels/{parcel.Id}/staff-reports/{report.Id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.GetAsync($"/api/v1/acquisition-requests/{request.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PostAsJsonAsync($"/api/v1/acquisition-requests/{request.Id}/approve", new { }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.GetAsync($"/api/v1/projects/{parcel.ProjectId}/acquisition-plan")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PostAsJsonAsync("/api/v1/acquisition-requests", Req(parcel.ProjectId), Json)).StatusCode.Should().Be(HttpStatusCode.NotFound, "another agency's project");
        (await Ok<PagedResult<RightOfEntryReportRowDto>>(await theirs.Http.GetAsync("/api/v1/rights-of-entry"))).Items.Should().NotContain(r => r.Id == roe.Id);
        (await Ok<PagedResult<AcquisitionRequestDto>>(await theirs.Http.GetAsync("/api/v1/acquisition-requests"))).Items.Should().NotContain(r => r.Id == request.Id);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from rights_of_entry where granted_by = 'Intruder'")).Should().Be(0);
    }

    [PgFact]
    public async Task A_split_parcel_keeps_its_records_for_reading_but_takes_no_new_ones()
    {
        var pm = await _api.As("ProjectManager");
        var parcel = await NewParcel(pm);
        await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry", Roe(), Json);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/split", new { parts = new[] { new { areaSqFt = 40m } } }, Json)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await Ok<List<RightOfEntryDto>>(await pm.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry"))).Should().ContainSingle();
        foreach (var res in new[]
        {
            await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry", Roe(), Json),
            await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/plan-reviews", new { discipline = "Engineer", outcome = "Approved", reviewedOn = Today(), reviewerName = "x" }, Json),
            await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/staff-reports", new { kind = "StaffReport", title = "x", body = "x" }, Json),
        })
        {
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await res.Content.ReadAsStringAsync()).Should().Contain("parcel_not_active");
        }
        (await Ok<PagedResult<RightOfEntryReportRowDto>>(await pm.Http.GetAsync($"/api/v1/rights-of-entry?projectId={parcel.ProjectId}"))).Items.Should().NotContain(r => r.ParcelId == parcel.Id, "the report lists live parcels");
    }

    [PgFact]
    public async Task The_tables_have_the_indexes_constraints_and_column_the_code_relies_on_and_the_database_enforces_them()
    {
        var pm = await _api.As("ProjectManager");
        var parcel = await NewParcel(pm);
        var indexes = (await PgTestDatabase.QueryAsync(_api.Database,
            "select indexname from pg_indexes where tablename in ('rights_of_entry', 'acquisition_requests', 'parcel_plan_reviews', 'parcel_staff_reports')")).Select(r => (string)r[0]!).ToList();
        indexes.Should().Contain(new[] { "ix_parcel_staff_reports_parcel_id_kind", "ix_rights_of_entry_parcel_id", "ix_parcel_plan_reviews_parcel_id_discipline" });
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from information_schema.columns where table_name = 'agency_settings' and column_name = 'plan_review_gate_enabled'")).Should().Be(1);

        const string roe = "insert into rights_of_entry (id, tenant_id, parcel_id, status, granted_on, expires_on, created_at, created_by, updated_at, updated_by) " +
                           "values (gen_random_uuid(), @t, @p, 'Granted', '2026-03-10', '2026-03-01', now(), @t, now(), @t)";
        var backwards = () => PgTestDatabase.QueryAsync(_api.Database, roe, ("t", pm.TenantId), ("p", parcel.Id));
        (await backwards.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23514", "ends before it was given");

        const string report = "insert into parcel_staff_reports (id, tenant_id, parcel_id, kind, status, title, body, created_at, created_by, updated_at, updated_by) " +
                              "values (gen_random_uuid(), @t, @p, 'StaffReport', @s, 'T', 'B', now(), @t, now(), @t)";
        await PgTestDatabase.QueryAsync(_api.Database, report, ("t", pm.TenantId), ("p", parcel.Id), ("s", "Rejected"));
        await PgTestDatabase.QueryAsync(_api.Database, report, ("t", pm.TenantId), ("p", parcel.Id), ("s", "Rejected"));
        await PgTestDatabase.QueryAsync(_api.Database, report, ("t", pm.TenantId), ("p", parcel.Id), ("s", "Draft"));
        var again = () => PgTestDatabase.QueryAsync(_api.Database, report, ("t", pm.TenantId), ("p", parcel.Id), ("s", "Approved"));
        (await again.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23505", "only one live report of a kind");
    }

    [PgTheory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"status\":\"Nope\"}")]
    [InlineData("{\"status\":\"Granted\",\"grantedOn\":\"not a date\"}")]
    [InlineData("{\"status\":\"Granted\",\"grantedOn\":\"1900-01-01\"}")]
    public async Task A_malformed_right_of_entry_body_is_a_400_never_a_500(string body)
    {
        var pm = await _api.As("ProjectManager");
        var parcel = await NewParcel(pm);
        var res = await pm.PostJson($"/api/v1/parcels/{parcel.Id}/rights-of-entry", body);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest, await res.Content.ReadAsStringAsync());
    }

    [PgFact]
    public async Task Hostile_and_oversized_input_is_stored_as_text_or_refused_cleanly()
    {
        var pm = await _api.As("ProjectManager");
        var parcel = await NewParcel(pm);
        var script = "<script>alert('x')</script>";

        (await Ok<StaffReportDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/staff-reports", new { kind = "StaffReport", title = script, body = "Robert'); DROP TABLE parcels;--" }, Json), HttpStatusCode.Created)).Title.Should().Be(script);
        (await Ok<AcquisitionRequestDto>(await pm.Http.PostAsJsonAsync("/api/v1/acquisition-requests", new { requesterType = "External", requesterName = script, property = "x' OR '1'='1" }, Json), HttpStatusCode.Created)).RequesterName.Should().Be(script);

        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry", new { status = "Requested", purpose = new string('x', 501) }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/rights-of-entry", new { status = "Requested", grantedBy = new string('x', 201) }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/staff-reports", new { kind = "CertificateOfNecessity", title = "T", body = new string('x', 8001) }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/staff-reports", new { kind = "Wizard", title = "T", body = "B" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/plan-reviews", new { discipline = "Wizard", outcome = "Approved", reviewedOn = Today(), reviewerName = "x" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync("/api/v1/acquisition-requests", new { requesterType = "Internal", requesterName = "x", property = "y", requesterEmail = "nope" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync("/api/v1/acquisition-requests", new { requesterType = "Internal", requesterName = "x", property = "y", estimatedAmount = -5 }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.PostJson("/api/v1/acquisition-requests", JsonSerializer.Serialize(new { requesterType = "Internal", requesterName = "null\0char", property = "y" }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync("/api/v1/acquisition-requests?status=Bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [PgFact]
    public async Task The_demo_data_includes_rights_of_entry_a_plan_review_a_report_and_requests_and_the_gate_is_off()
    {
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from rights_of_entry re join parcels p on p.id = re.parcel_id join projects pr on pr.id = p.project_id where pr.number = 'DEMO-002'")).Should().Be(2);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_plan_reviews")).Should().BeGreaterThanOrEqualTo(1);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_staff_reports where status = 'Draft'")).Should().BeGreaterThanOrEqualTo(1);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from acquisition_requests where status = 'Submitted'")).Should().BeGreaterThanOrEqualTo(2);
        var pm = await _api.As("ProjectManager");
        var report = await Ok<PagedResult<RightOfEntryReportRowDto>>(await pm.Http.GetAsync("/api/v1/rights-of-entry?expiringWithinDays=30"));
        report.Items.Should().Contain(r => r.DaysUntilExpiry == 20, "the demo right ends in 20 days");
    }
}
