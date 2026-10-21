using System.Net;
using System.Text;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>
/// The backend half of the per-role acceptance cases (vault/acceptance/*.md). Every role is checked
/// against the hand-written spec table, on real Postgres, for every page-governed capability that exists today.
/// </summary>
public class RoleAcceptanceApiTests : IClassFixture<PgApi>
{
    private readonly PgApi _api;
    public RoleAcceptanceApiTests(PgApi api) => _api = api;

    private static HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;

    // ---- the full capability matrix ----------------------------------------------------------

    [PgTheory]
    [MemberData(nameof(Spec.RoleAndEntityType), MemberType = typeof(Spec))]
    public async Task Attachments_on_every_kind_of_record_follow_exactly_the_role_spec(string role, string entityType)
    {
        var page = Spec.AttachmentPage[entityType];
        var entity = Guid.NewGuid();
        var original = Encoding.UTF8.GetBytes($"original for {entityType}");

        // Admin (full access everywhere) puts a file on the record first.
        var admin = await _api.As("Admin");
        var seeded = await admin.Upload(entityType, entity, "original.pdf", original);
        seeded.StatusCode.Should().Be(HttpStatusCode.Created);
        var seededId = (await admin.ReadJson(seeded)).GetProperty("id").GetGuid();

        var user = await _api.As(role);

        // See what is attached: needs Read on the owning page.
        (await user.Http.GetAsync($"/api/v1/documents?entityType={entityType}&entityId={entity}")).StatusCode
            .Should().Be(Expect(Spec.Can(role, page, "read"), HttpStatusCode.OK), $"{role} list {entityType}");

        // Open a file: Read.
        var download = await user.Http.GetAsync($"/api/v1/documents/{seededId}/content");
        download.StatusCode.Should().Be(Expect(Spec.Can(role, page, "read"), HttpStatusCode.OK), $"{role} download {entityType}");
        if (download.StatusCode == HttpStatusCode.OK)
            (await download.Content.ReadAsByteArrayAsync()).Should().Equal(original);

        // Attach a new file: Update.
        (await user.Upload(entityType, entity, "mine.pdf", Encoding.UTF8.GetBytes("mine"))).StatusCode
            .Should().Be(Expect(Spec.Can(role, page, "update"), HttpStatusCode.Created), $"{role} attach to {entityType}");

        // Remove a file: Delete.
        var delete = await user.Http.DeleteAsync($"/api/v1/documents/{seededId}");
        delete.StatusCode.Should().Be(Expect(Spec.Can(role, page, "delete"), HttpStatusCode.NoContent), $"{role} remove from {entityType}");

        // And what happened is visible to the next person: removed only if the delete was allowed.
        var after = await admin.Http.GetAsync($"/api/v1/documents/{seededId}/content");
        after.StatusCode.Should().Be(Spec.Can(role, page, "delete") ? HttpStatusCode.NotFound : HttpStatusCode.OK);
    }

    [PgTheory]
    [MemberData(nameof(Spec.RoleAndPage), MemberType = typeof(Spec))]
    public async Task Saved_filters_can_be_kept_on_exactly_the_pages_a_role_can_open(string role, string page)
    {
        var user = await _api.As(role);
        var body = System.Text.Json.JsonSerializer.Serialize(new { pageKey = page, name = "My view", filterJson = "{\"status\":\"Open\"}" });

        var create = await user.PostJson("/api/v1/saved-filters", body);
        var list = await user.Http.GetAsync($"/api/v1/saved-filters?pageKey={page}");

        create.StatusCode.Should().Be(Expect(Spec.Can(role, page, "read"), HttpStatusCode.Created), $"{role} save filter on {page}");
        list.StatusCode.Should().Be(Expect(Spec.Can(role, page, "read"), HttpStatusCode.OK), $"{role} list filters on {page}");
    }

    [PgTheory]
    [MemberData(nameof(Spec.EachRole), MemberType = typeof(Spec))]
    public async Task Every_role_can_sign_in_and_see_who_they_are(string role)
    {
        var user = await _api.As(role);

        var me = await user.ReadJson(await user.Http.GetAsync("/api/v1/auth/me"));

        me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal(role);
        me.GetProperty("tenantId").GetGuid().Should().Be(user.TenantId);
    }

    // ---- the hand-offs between roles (read as the story each job tells) -----------------------

    [PgFact]
    public async Task Title_package_hand_off_the_examiner_attaches_and_everyone_downstream_can_read_but_not_change()
    {
        var parcel = Guid.NewGuid();
        var examiner = await _api.As("TitleResearcher");
        var commitment = await examiner.Upload("TitleRecord", parcel, "commitment.pdf", Encoding.UTF8.GetBytes("commitment"), "TitleCommitment");
        var cad = await examiner.Upload("SurveyRequest", parcel, "boundary.dwg", new byte[] { 1, 2, 3 }, "BoundarySurvey");
        commitment.StatusCode.Should().Be(HttpStatusCode.Created);
        cad.StatusCode.Should().Be(HttpStatusCode.Created);
        var commitmentId = (await examiner.ReadJson(commitment)).GetProperty("id").GetGuid();

        // Appraiser, Project Manager and Negotiator can read the title record; none of them can alter it.
        foreach (var role in new[] { "Appraiser", "ProjectManager", "Negotiator" })
        {
            var reader = await _api.As(role);
            (await reader.Http.GetAsync($"/api/v1/documents/{commitmentId}/content")).StatusCode.Should().Be(HttpStatusCode.OK, role);
            (await reader.Upload("TitleRecord", parcel, "sneaky.pdf", new byte[] { 9 })).StatusCode.Should().Be(HttpStatusCode.Forbidden, role);
            (await reader.Http.DeleteAsync($"/api/v1/documents/{commitmentId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, role);
        }

        // Finance has no business in title: it cannot even see it.
        var finance = await _api.As("Finance");
        (await finance.Http.GetAsync($"/api/v1/documents?entityType=TitleRecord&entityId={parcel}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await finance.Http.GetAsync($"/api/v1/documents/{commitmentId}/content")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The examiner can still withdraw their own upload.
        (await examiner.Http.DeleteAsync($"/api/v1/documents/{commitmentId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [PgFact]
    public async Task Appraisal_hand_off_the_appraiser_attaches_the_report_and_the_negotiator_can_use_it()
    {
        var parcel = Guid.NewGuid();
        var appraiser = await _api.As("Appraiser");
        var report = await appraiser.Upload("Appraisal", parcel, "appraisal-report.pdf", Encoding.UTF8.GetBytes("value: 1,250,000"), "AppraisalReport");
        report.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await appraiser.ReadJson(report)).GetProperty("id").GetGuid();

        var negotiator = await _api.As("Negotiator");
        (await negotiator.Http.GetAsync($"/api/v1/documents/{id}/content")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await negotiator.Upload("Appraisal", parcel, "edit.pdf", new byte[] { 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The title examiner can read appraisals (to see the package outcome) but not alter them.
        var examiner = await _api.As("TitleResearcher");
        (await examiner.Http.GetAsync($"/api/v1/documents/{id}/content")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await examiner.Http.DeleteAsync($"/api/v1/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [PgFact]
    public async Task Offer_agreement_and_closing_the_negotiator_works_the_deal_and_finance_can_see_it()
    {
        var parcel = Guid.NewGuid();
        var negotiator = await _api.As("Negotiator");
        foreach (var (type, file) in new[] { ("Offer", "offer-letter.pdf"), ("Agreement", "signed-agreement.pdf"), ("ClosingRecord", "closing-checklist.pdf") })
            (await negotiator.Upload(type, parcel, file, Encoding.UTF8.GetBytes(file))).StatusCode.Should().Be(HttpStatusCode.Created, type);

        // The negotiator can correct a closing record (update) but cannot delete one (Standard, not Pro).
        var closing = await negotiator.ReadJson(await negotiator.Http.GetAsync($"/api/v1/documents?entityType=ClosingRecord&entityId={parcel}"));
        var closingId = closing[0].GetProperty("id").GetGuid();
        (await negotiator.Http.DeleteAsync($"/api/v1/documents/{closingId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Finance sees offers, agreements and closing records, but changes none of them.
        var finance = await _api.As("Finance");
        foreach (var type in new[] { "Offer", "Agreement", "ClosingRecord" })
        {
            (await finance.Http.GetAsync($"/api/v1/documents?entityType={type}&entityId={parcel}")).StatusCode.Should().Be(HttpStatusCode.OK, type);
            (await finance.Upload(type, parcel, "x.pdf", new byte[] { 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden, type);
        }
    }

    [PgFact]
    public async Task Payment_records_finance_owns_them_and_the_negotiator_can_add_but_never_remove()
    {
        var parcel = Guid.NewGuid();
        var finance = await _api.As("Finance");
        var created = await finance.Upload("Payment", parcel, "payment-request.pdf", Encoding.UTF8.GetBytes("pay"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await finance.ReadJson(created)).GetProperty("id").GetGuid();

        var negotiator = await _api.As("Negotiator");
        (await negotiator.Upload("Payment", parcel, "supporting.pdf", new byte[] { 1 })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await negotiator.Http.DeleteAsync($"/api/v1/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Finance sees the negotiator's supporting paper next to its own request.
        (await finance.ReadJson(await finance.Http.GetAsync($"/api/v1/documents?entityType=Payment&entityId={parcel}"))).GetArrayLength().Should().Be(2);

        (await finance.Http.DeleteAsync($"/api/v1/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [PgFact]
    public async Task Project_and_parcel_records_the_project_manager_owns_them_and_specialists_only_read()
    {
        var parcel = Guid.NewGuid();
        var pm = await _api.As("ProjectManager");
        (await pm.Upload("Project", Guid.NewGuid(), "funding-approval.pdf", new byte[] { 1 })).StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await pm.Upload("Parcel", parcel, "parcel-sketch.pdf", new byte[] { 1 });
        var id = (await pm.ReadJson(created)).GetProperty("id").GetGuid();

        foreach (var role in new[] { "TitleResearcher", "Appraiser", "Negotiator", "Finance" })
        {
            var user = await _api.As(role);
            (await user.Http.GetAsync($"/api/v1/documents/{id}/content")).StatusCode.Should().Be(HttpStatusCode.OK, role);
            (await user.Upload("Parcel", parcel, "x.pdf", new byte[] { 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden, role);
        }
    }

    [PgFact]
    public async Task Owners_and_interest_holders_the_negotiator_captures_them_and_others_read()
    {
        var party = Guid.NewGuid();
        var negotiator = await _api.As("Negotiator");
        var upload = await negotiator.Upload("Party", party, "w9.pdf", new byte[] { 1 }, "TaxForm");
        upload.StatusCode.Should().Be(HttpStatusCode.Created);

        var examiner = await _api.As("Appraiser");
        (await examiner.Http.GetAsync($"/api/v1/documents?entityType=Party&entityId={party}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await examiner.Upload("Party", party, "x.pdf", new byte[] { 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [PgFact]
    public async Task Admin_can_do_everything_to_everything()
    {
        var admin = await _api.As("Admin");
        foreach (var type in Spec.AttachmentPage.Keys)
        {
            var entity = Guid.NewGuid();
            var up = await admin.Upload(type, entity, "a.pdf", new byte[] { 1, 2 });
            up.StatusCode.Should().Be(HttpStatusCode.Created, type);
            var id = (await admin.ReadJson(up)).GetProperty("id").GetGuid();
            (await admin.Http.GetAsync($"/api/v1/documents/{id}/content")).StatusCode.Should().Be(HttpStatusCode.OK, type);
            (await admin.Http.DeleteAsync($"/api/v1/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent, type);
        }
        (await admin.Http.GetAsync("/api/v1/admin/ping")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- per-user privacy ---------------------------------------------------------------------

    [PgFact]
    public async Task One_users_saved_views_are_invisible_to_a_colleague_with_the_same_role()
    {
        var ann = await _api.As("ProjectManager", user: Guid.NewGuid());
        var ben = await _api.As("ProjectManager", user: Guid.NewGuid());
        (await ann.PostJson("/api/v1/saved-filters",
            "{\"pageKey\":\"parcels\",\"name\":\"Ann only\",\"filterJson\":\"{}\"}")).StatusCode.Should().Be(HttpStatusCode.Created);

        var bens = await ben.ReadJson(await ben.Http.GetAsync("/api/v1/saved-filters?pageKey=parcels"));

        bens.GetArrayLength().Should().Be(0);
    }
}
