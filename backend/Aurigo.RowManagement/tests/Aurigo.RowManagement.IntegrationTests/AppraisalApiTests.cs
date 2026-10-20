using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.ParcelTabs;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>Phase 1.5 slice 6c (ADR 0018): a parcel's Appraisal tab on the real API and real Postgres. Matches AT-AP-40, AT-AA-40, AT-X-51.</summary>
public class AppraisalApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly PgApi _api;
    public AppraisalApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> t) => (await t).StatusCode;

    private async Task<ParcelDto> NewParcel()
    {
        var admin = await _api.As("Admin");
        var project = await Ok<ProjectDto>(await admin.Http.PostAsJsonAsync("/api/v1/projects", new { name = $"AP {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local" }, Json), HttpStatusCode.Created);
        return await Ok<ParcelDto>(await admin.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 100m, acquisitionType = "Standard" }, Json), HttpStatusCode.Created);
    }

    private static Dictionary<string, object?> Body(Dictionary<string, object?>? extra = null)
    {
        var b = new Dictionary<string, object?>
        {
            ["appraisalType"] = "Full Appraisal", ["appraiser"] = "Ann Appraiser", ["landPlannerRequired"] = true, ["assignedTo"] = "Casey",
            ["requestedDate"] = "2026-02-01", ["receivedDate"] = "2026-02-20", ["reportDate"] = "2026-02-18", ["dueDate"] = "2026-03-01",
            ["submittedToAtpDate"] = "2026-02-21", ["effectiveDate"] = "2026-02-10",
            ["amounts"] = new[] { new { amountType = "Land", description = (string?)"Fee simple", amount = 100000m }, new { amountType = "Damages", description = (string?)null, amount = 2500.25m } },
            ["submittedToJatDate"] = "2026-03-02", ["approvedByJatDate"] = "2026-03-05", ["ftaConcurrenceNeeded"] = true,
        };
        if (extra is not null) foreach (var kv in extra) b[kv.Key] = kv.Value;
        return b;
    }

    private static Dictionary<string, object?> Review(Dictionary<string, object?>? extra = null)
    {
        var b = Body();
        b["reviewAppraiser"] = "Rita Reviewer"; b["reviewType"] = "Desk Review"; b["appraisalReceivedDate"] = "2026-02-20";
        b["approvedJustCompensation"] = 101000m; b["reviewCompletedDate"] = "2026-02-25"; b["reviewSubmittedDate"] = "2026-02-26";
        b["reviewRecommendation"] = "Approve"; b["reviewRecommendedValue"] = 102000m;
        if (extra is not null) foreach (var kv in extra) b[kv.Key] = kv.Value;
        return b;
    }

    [PgFact]
    public async Task AT_AP_40_An_appraiser_adds_changes_and_removes_appraisals_numbered_per_parcel()
    {
        var parcel = await NewParcel();
        var ap = await _api.As("Appraiser");
        var url = $"/api/v1/parcels/{parcel.Id}/appraisals";

        var first = await Ok<AppraisalDto>(await ap.Http.PostAsJsonAsync(url, Body(), Json), HttpStatusCode.Created);
        var second = await Ok<AppraisalDto>(await ap.Http.PostAsJsonAsync(url, Body(new() { ["appraisalType"] = "Review" }), Json), HttpStatusCode.Created);
        first.Number.Should().Be($"APR-{parcel.Number}-001");
        second.Number.Should().Be($"APR-{parcel.Number}-002");
        first.AppraisalTotal.Should().Be(102500.25m, "the total is the amounts added up");
        first.Amounts.Should().HaveCount(2);
        first.LandPlannerRequired.Should().BeTrue();
        first.FtaConcurrenceNeeded.Should().BeTrue();
        first.ApprovedJustCompensation.Should().BeNull();

        var changed = await Ok<AppraisalDto>(await ap.Http.PutAsJsonAsync($"{url}/{first.Id}", Body(new() { ["appraiser"] = "Other", ["amounts"] = new[] { new { amountType = "Land", description = (string?)null, amount = 5m } } }), Json));
        changed.Appraiser.Should().Be("Other");
        changed.Amounts.Should().ContainSingle();
        changed.AppraisalTotal.Should().Be(5m);
        changed.Number.Should().Be(first.Number);

        (await Ok<List<AppraisalDto>>(await ap.Http.GetAsync(url))).Select(a => a.Number).Should().Equal(first.Number, second.Number);

        (await Status(ap.Http.DeleteAsync($"{url}/{second.Id}"))).Should().Be(HttpStatusCode.NoContent);
        (await Status(ap.Http.DeleteAsync($"{url}/{second.Id}"))).Should().Be(HttpStatusCode.NotFound);
        (await Ok<List<AppraisalDto>>(await ap.Http.GetAsync(url))).Should().ContainSingle();
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_appraisal_amounts a join parcel_appraisals p on p.id = a.appraisal_id where p.parcel_id = @p", ("p", parcel.Id))).Should().Be(1);
    }

    [PgFact]
    public async Task AT_AP_40_The_appraiser_company_is_read_from_the_Library_agent()
    {
        var parcel = await NewParcel();
        var admin = await _api.As("Admin");
        var name = $"Agent {Guid.NewGuid().ToString("N")[..6]}";
        (await Status(admin.Http.PostAsJsonAsync("/api/v1/library/agents", new { name, agentType = "Appraiser", company = "Acme Appraisals", email = "a@example.com", phone = "555-0100" }, Json))).Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var made = await Ok<AppraisalDto>(await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/appraisals", Body(new() { ["appraiser"] = name }), Json), HttpStatusCode.Created);

        made.AppraiserCompany.Should().Be("Acme Appraisals");
    }

    [PgFact]
    public async Task AT_AP_40_Ten_adds_at_once_are_numbered_one_to_ten()
    {
        var parcel = await NewParcel();
        var ap = await _api.As("Appraiser");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ap.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/appraisals", Body(), Json)));

        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var numbers = (await Ok<List<AppraisalDto>>(await ap.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/appraisals"))).Select(a => a.Number).ToList();
        numbers.Should().OnlyHaveUniqueItems().And.HaveCount(10);
    }

    [PgFact]
    public async Task AT_AP_40_Appraisal_input_is_checked()
    {
        var parcel = await NewParcel();
        var ap = await _api.As("Appraiser");
        var url = $"/api/v1/parcels/{parcel.Id}/appraisals";

        (await Status(ap.Http.PostAsJsonAsync(url, Body(new() { ["requestedDate"] = "2026-03-01", ["receivedDate"] = "2026-02-01" }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(ap.Http.PostAsJsonAsync(url, Body(new() { ["submittedToJatDate"] = "2026-03-05", ["approvedByJatDate"] = "2026-03-01" }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(ap.Http.PostAsJsonAsync(url, Body(new() { ["amounts"] = new[] { new { amountType = "Land", description = (string?)null, amount = -1m } } }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(ap.Http.PostAsJsonAsync(url, Body(new() { ["amounts"] = new[] { new { amountType = "", description = (string?)null, amount = 1m } } }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(ap.Http.PostAsJsonAsync(url, Body(new() { ["amounts"] = Enumerable.Range(0, 51).Select(_ => new { amountType = "Land", description = (string?)null, amount = 1m }).ToArray() }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(ap.Http.PostAsJsonAsync(url, Body(new() { ["appraiser"] = new string('a', 201) }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(ap.Http.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/appraisals"))).Should().Be(HttpStatusCode.NotFound);
        (await Status(ap.Http.PutAsJsonAsync($"{url}/{Guid.NewGuid()}", Body(), Json))).Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_appraisals where parcel_id = @p", ("p", parcel.Id))).Should().Be(0, "a refused save creates nothing");
    }

    [PgFact]
    public async Task AT_AA_40_Only_the_approver_sets_the_review_and_the_approved_just_compensation()
    {
        var parcel = await NewParcel();
        var ap = await _api.As("Appraiser");
        var aa = await _api.As("AppraisalApprover");
        var url = $"/api/v1/parcels/{parcel.Id}/appraisals";

        // The appraiser cannot start an appraisal with a review filled in.
        (await Status(ap.Http.PostAsJsonAsync(url, Review(), Json))).Should().Be(HttpStatusCode.Forbidden);
        var made = await Ok<AppraisalDto>(await ap.Http.PostAsJsonAsync(url, Body(), Json), HttpStatusCode.Created);

        // ...nor change the review later.
        (await Status(ap.Http.PutAsJsonAsync($"{url}/{made.Id}", Review(), Json))).Should().Be(HttpStatusCode.Forbidden);
        (await Status(ap.Http.PutAsJsonAsync($"{url}/{made.Id}", Body(new() { ["approvedJustCompensation"] = 1m }), Json))).Should().Be(HttpStatusCode.Forbidden);

        // The approver sets it.
        var reviewed = await Ok<AppraisalDto>(await aa.Http.PutAsJsonAsync($"{url}/{made.Id}", Review(), Json));
        reviewed.ApprovedJustCompensation.Should().Be(101000m);
        reviewed.ReviewAppraiser.Should().Be("Rita Reviewer");
        reviewed.ReviewRecommendation.Should().Be("Approve");
        reviewed.ReviewRecommendedValue.Should().Be(102000m);

        // The appraiser can still save the appraisal, sending the review back as it is.
        var saved = await Ok<AppraisalDto>(await ap.Http.PutAsJsonAsync($"{url}/{made.Id}", Review(new() { ["assignedTo"] = "Dana" }), Json));
        saved.AssignedTo.Should().Be("Dana");
        saved.ApprovedJustCompensation.Should().Be(101000m);

        // Sending the review blank does not wipe it for a role that cannot change it.
        (await Status(ap.Http.PutAsJsonAsync($"{url}/{made.Id}", Body(), Json))).Should().Be(HttpStatusCode.Forbidden);
        (await Ok<List<AppraisalDto>>(await ap.Http.GetAsync(url))).Single().ApprovedJustCompensation.Should().Be(101000m);

        // The approver may change what the appraiser filled in too (update access), and a negative amount is refused.
        (await Status(aa.Http.PutAsJsonAsync($"{url}/{made.Id}", Review(new() { ["approvedJustCompensation"] = -5m }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Ok<AppraisalDto>(await aa.Http.PutAsJsonAsync($"{url}/{made.Id}", Review(new() { ["appraiser"] = "Changed by approver" }), Json))).Appraiser.Should().Be("Changed by approver");
    }

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task AT_X_51_Each_role_can_do_exactly_what_the_Appraisal_page_allows(string role)
    {
        var parcel = await NewParcel();
        var admin = await _api.As("Admin");
        var url = $"/api/v1/parcels/{parcel.Id}/appraisals";
        var seed = await Ok<AppraisalDto>(await admin.Http.PostAsJsonAsync(url, Body(), Json), HttpStatusCode.Created);
        var doomed = await Ok<AppraisalDto>(await admin.Http.PostAsJsonAsync(url, Body(), Json), HttpStatusCode.Created);
        var user = await _api.As(role);
        bool Can(string action) => Spec.Can(role, "parcelappraisal", action);
        HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;

        (await Status(user.Http.GetAsync(url))).Should().Be(Expect(Can("read"), HttpStatusCode.OK));
        (await Status(user.Http.PostAsJsonAsync(url, Body(), Json))).Should().Be(Expect(Can("create"), HttpStatusCode.Created));
        (await Status(user.Http.PutAsJsonAsync($"{url}/{seed.Id}", Body(new() { ["assignedTo"] = $"by {role}" }), Json))).Should().Be(Expect(Can("update"), HttpStatusCode.OK));
        (await Status(user.Http.DeleteAsync($"{url}/{doomed.Id}"))).Should().Be(Expect(Can("delete"), HttpStatusCode.NoContent));
    }

    public static IEnumerable<object[]> Roles() => Spec.Roles.Select(r => new object[] { r });

    [PgFact]
    public async Task Without_signing_in_the_appraisals_are_not_available()
    {
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/appraisals")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
