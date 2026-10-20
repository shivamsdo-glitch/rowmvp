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

/// <summary>Phase 1.5 slice 6a (ADR 0018): a parcel's Title Encumbrances on the real API and real Postgres. Matches AT-TE-40, AT-X-49.</summary>
public class EncumbranceApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly PgApi _api;
    public EncumbranceApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> t) => (await t).StatusCode;

    private async Task<ParcelDto> NewParcel()
    {
        var admin = await _api.As("Admin");
        var project = await Ok<ProjectDto>(await admin.Http.PostAsJsonAsync("/api/v1/projects", new { name = $"Enc {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local" }, Json), HttpStatusCode.Created);
        return await Ok<ParcelDto>(await admin.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 100m, acquisitionType = "Standard" }, Json), HttpStatusCode.Created);
    }

    private static Dictionary<string, object?> Body(Dictionary<string, object?>? extra = null)
    {
        var b = new Dictionary<string, object?>
        {
            ["encumbranceType"] = "Mortgage Lien", ["documentTitle"] = "Deed of Trust 2019", ["documentDate"] = "2019-03-01", ["dateRecorded"] = "2019-03-15",
            ["documentNumber"] = "Vol 12 Pg 345", ["isUnrecorded"] = false, ["grantor"] = "Kamali Family", ["grantee"] = "First Bank",
            ["exceptionSubmittedDate"] = "2026-02-01", ["exceptionApprovedDate"] = "2026-02-10", ["impactAnalysis"] = "Blocks closing", ["recommendedCure"] = "Obtain a release",
            ["cureStatus"] = "In progress", ["reviewNotes"] = "Checked county records",
        };
        if (extra is not null) foreach (var kv in extra) b[kv.Key] = kv.Value;
        return b;
    }

    [PgFact]
    public async Task AT_TE_40_A_title_researcher_records_changes_and_removes_encumbrances_numbered_by_parcel()
    {
        var parcel = await NewParcel();
        var tr = await _api.As("TitleResearcher");
        var url = $"/api/v1/parcels/{parcel.Id}/encumbrances";

        var first = await Ok<EncumbranceDto>(await tr.Http.PostAsJsonAsync(url, Body(new() { ["links"] = new[] { new { recordIdentifier = "TC-1", moduleName = "Title and Closing", notes = (string?)null } } }), Json), HttpStatusCode.Created);
        first.Number.Should().Be($"ENC-{parcel.Number}-001");
        first.EncumbranceType.Should().Be("Mortgage Lien");
        first.DateRecorded.Should().Be(new DateOnly(2019, 3, 15));
        first.ExceptionApprovedDate.Should().Be(new DateOnly(2026, 2, 10));
        first.Links.Should().ContainSingle().Which.RecordIdentifier.Should().Be("TC-1");

        var second = await Ok<EncumbranceDto>(await tr.Http.PostAsJsonAsync(url, Body(new() { ["documentTitle"] = "Road easement", ["encumbranceType"] = "Easement - Access" }), Json), HttpStatusCode.Created);
        second.Number.Should().Be($"ENC-{parcel.Number}-002");
        (await Ok<List<EncumbranceDto>>(await tr.Http.GetAsync(url))).Select(e => e.Number).Should().Equal($"ENC-{parcel.Number}-001", $"ENC-{parcel.Number}-002");

        var changed = await Ok<EncumbranceDto>(await tr.Http.PutAsJsonAsync($"{url}/{first.Id}", Body(new() { ["cureStatus"] = "Cured", ["links"] = Array.Empty<object>() }), Json));
        changed.CureStatus.Should().Be("Cured");
        changed.Links.Should().BeEmpty("an update replaces the linked records");
        changed.Number.Should().Be(first.Number);

        (await Status(tr.Http.DeleteAsync($"{url}/{first.Id}"))).Should().Be(HttpStatusCode.NoContent);
        (await Ok<List<EncumbranceDto>>(await tr.Http.GetAsync(url))).Should().ContainSingle().Which.Id.Should().Be(second.Id);
        var third = await Ok<EncumbranceDto>(await tr.Http.PostAsJsonAsync(url, Body(), Json), HttpStatusCode.Created);
        third.Number.Should().Be($"ENC-{parcel.Number}-003", "removing an earlier encumbrance never makes a number come round again");
    }

    [PgFact]
    public async Task AT_TE_40_An_unrecorded_document_has_no_recording_date_and_ten_added_at_once_are_numbered_apart()
    {
        var parcel = await NewParcel();
        var tr = await _api.As("TitleResearcher");
        var url = $"/api/v1/parcels/{parcel.Id}/encumbrances";

        var bad = await tr.Http.PostAsJsonAsync(url, Body(new() { ["isUnrecorded"] = true }), Json);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("An unrecorded document has no recording date.");
        var ok = await Ok<EncumbranceDto>(await tr.Http.PostAsJsonAsync(url, Body(new() { ["isUnrecorded"] = true, ["dateRecorded"] = null }), Json), HttpStatusCode.Created);
        ok.IsUnrecorded.Should().BeTrue();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => tr.Http.PostAsJsonAsync(url, Body(new() { ["documentTitle"] = $"Doc {i}" }), Json)));
        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(distinct sequence) from parcel_encumbrances where parcel_id = @p", ("p", parcel.Id))).Should().Be(11);
    }

    [PgFact]
    public async Task AT_TE_40_Encumbrance_input_is_checked()
    {
        var parcel = await NewParcel();
        var tr = await _api.As("TitleResearcher");
        var url = $"/api/v1/parcels/{parcel.Id}/encumbrances";

        foreach (var field in new[] { "encumbranceType", "documentTitle", "grantor", "grantee" })
            (await Status(tr.Http.PostAsJsonAsync(url, Body(new() { [field] = "" }), Json))).Should().Be(HttpStatusCode.BadRequest, field);
        (await Status(tr.Http.PostAsJsonAsync(url, Body(new() { ["documentTitle"] = new string('t', 301) }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(tr.Http.PostAsJsonAsync(url, Body(new() { ["impactAnalysis"] = new string('i', 4001) }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(tr.Http.PostAsJsonAsync(url, Body(new() { ["exceptionSubmittedDate"] = "2026-03-01", ["exceptionApprovedDate"] = "2026-02-01" }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(tr.Http.PostAsJsonAsync(url, Body(new() { ["links"] = new[] { new { recordIdentifier = "", moduleName = "x" } } }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(tr.Http.PutAsJsonAsync($"{url}/{Guid.NewGuid()}", Body(), Json))).Should().Be(HttpStatusCode.NotFound);
        (await Status(tr.Http.DeleteAsync($"{url}/{Guid.NewGuid()}"))).Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_encumbrances where parcel_id = @p", ("p", parcel.Id))).Should().Be(0, "a refused request saves nothing");
    }

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task AT_X_49_Each_role_can_do_exactly_what_the_Title_Encumbrances_page_allows(string role)
    {
        var parcel = await NewParcel();
        var admin = await _api.As("Admin");
        var url = $"/api/v1/parcels/{parcel.Id}/encumbrances";
        var seed = await Ok<EncumbranceDto>(await admin.Http.PostAsJsonAsync(url, Body(), Json), HttpStatusCode.Created);
        var user = await _api.As(role);
        bool Can(string action) => Spec.Can(role, "titleencumbrances", action);
        HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;

        (await Status(user.Http.GetAsync(url))).Should().Be(Expect(Can("read"), HttpStatusCode.OK));
        (await Status(user.Http.PostAsJsonAsync(url, Body(new() { ["documentTitle"] = $"By {role}" }), Json))).Should().Be(Expect(Can("create"), HttpStatusCode.Created));
        (await Status(user.Http.PutAsJsonAsync($"{url}/{seed.Id}", Body(new() { ["reviewNotes"] = $"edited by {role}" }), Json))).Should().Be(Expect(Can("update"), HttpStatusCode.OK));
        (await Status(user.Http.DeleteAsync($"{url}/{seed.Id}"))).Should().Be(Expect(Can("delete"), HttpStatusCode.NoContent));
    }

    public static IEnumerable<object[]> Roles() => Spec.Roles.Select(r => new object[] { r });

    [PgFact]
    public async Task Without_signing_in_encumbrances_are_not_available()
    {
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/encumbrances")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
