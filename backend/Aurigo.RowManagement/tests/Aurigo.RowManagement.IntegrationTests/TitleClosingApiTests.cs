using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.ParcelTabs;
using Aurigo.RowManagement.Application.Parties;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>Phase 1.5 slice 6b (ADR 0018): a parcel's Title and Closing record on the real API and real Postgres. Matches AT-TE-41, AT-NE-41, AT-X-50.</summary>
public class TitleClosingApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly PgApi _api;
    public TitleClosingApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private sealed record Envelope(TitleClosingDto? Record);

    private static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> t) => (await t).StatusCode;

    private async Task<ParcelDto> NewParcel()
    {
        var admin = await _api.As("Admin");
        var project = await Ok<ProjectDto>(await admin.Http.PostAsJsonAsync("/api/v1/projects", new { name = $"TC {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local" }, Json), HttpStatusCode.Created);
        return await Ok<ParcelDto>(await admin.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 100m, acquisitionType = "Standard" }, Json), HttpStatusCode.Created);
    }

    private static Dictionary<string, object?> Body(Dictionary<string, object?>? extra = null)
    {
        var b = new Dictionary<string, object?>
        {
            ["assignedTo"] = "Casey Closer", ["titleCompany"] = "Lone Star Title", ["titleClosingAgent"] = "Pat Agent", ["gfNumber"] = "GF-100", ["finalTitlePolicyNo"] = "FTP-7",
            ["psaSignedByOwnerDate"] = "2026-03-01", ["psaSignedByAtpDate"] = "2026-03-05", ["psaAmount"] = 125000.50m,
            ["settlementReceivedDate"] = "2026-04-01", ["settlementExecutedDate"] = "2026-04-03", ["curativeItemsCompleted"] = true,
            ["closingPackageSubmittedDate"] = "2026-04-10", ["closingPackageApprovedDate"] = "2026-04-12", ["closingInstructionDate"] = "2026-04-13",
            ["anticipatedClosingDate"] = "2026-05-01", ["actualClosingDate"] = "2026-05-02",
            ["conveyanceDocuments"] = new[] { new { conveyanceType = "Warranty Deed", signedDate = "2026-04-20", recordedDate = "2026-04-25", recordingNumber = "2026-0001" } },
            ["titleCommitments"] = new[] { new { vestedOwnerName = "Kamali", requestedDate = "2026-02-01", receivedDate = "2026-02-10", effectiveDate = "2026-02-09", issuedDate = "2026-02-11", curativePlanSubmittedDate = "2026-02-15", curativePlanApprovedDate = "2026-02-20" } },
        };
        if (extra is not null) foreach (var kv in extra) b[kv.Key] = kv.Value;
        return b;
    }

    [PgFact]
    public async Task AT_TE_41_A_title_researcher_saves_the_record_and_it_is_changed_and_removed_with_one_record_per_parcel()
    {
        var parcel = await NewParcel();
        var tr = await _api.As("TitleResearcher");
        var url = $"/api/v1/parcels/{parcel.Id}/title-closing";

        (await Ok<Envelope>(await tr.Http.GetAsync(url))).Record.Should().BeNull("nothing has been saved yet");

        var saved = await Ok<TitleClosingDto>(await tr.Http.PutAsJsonAsync(url, Body(), Json));
        saved.Number.Should().Be($"TC-{parcel.Number}");
        saved.ParcelNumber.Should().Be(parcel.Number);
        saved.TitleCompany.Should().Be("Lone Star Title");
        saved.PsaAmount.Should().Be(125000.50m);
        saved.CurativeItemsCompleted.Should().BeTrue();
        saved.ActualClosingDate.Should().Be(new DateOnly(2026, 5, 2));
        saved.ConveyanceDocuments.Should().ContainSingle().Which.RecordingNumber.Should().Be("2026-0001");
        saved.TitleCommitments.Should().ContainSingle().Which.VestedOwnerName.Should().Be("Kamali");

        var changed = await Ok<TitleClosingDto>(await tr.Http.PutAsJsonAsync(url, Body(new() { ["titleCompany"] = "Other Title", ["conveyanceDocuments"] = Array.Empty<object>(), ["actualClosingDate"] = null }), Json));
        changed.Id.Should().Be(saved.Id, "a second save changes the same record");
        changed.TitleCompany.Should().Be("Other Title");
        changed.ConveyanceDocuments.Should().BeEmpty("a save replaces the grids as sent");
        changed.TitleCommitments.Should().HaveCount(1);
        changed.ActualClosingDate.Should().BeNull();
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_title_closings where parcel_id = @p", ("p", parcel.Id))).Should().Be(1);

        var admin = await _api.As("Admin");
        (await Status(admin.Http.DeleteAsync(url))).Should().Be(HttpStatusCode.NoContent);
        (await Ok<Envelope>(await admin.Http.GetAsync(url))).Record.Should().BeNull();
        (await Status(admin.Http.DeleteAsync(url))).Should().Be(HttpStatusCode.NotFound);
    }

    [PgFact]
    public async Task AT_TE_41_The_vested_owner_name_comes_from_the_ownerships_of_type_Vested_Owner()
    {
        var parcel = await NewParcel();
        var designer = await _api.As("Designer");
        await Ok<PartyLinkDto>(await designer.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/parties", new { newParty = new { kind = "Person", name = "Fee Person" }, role = "FeeOwner", ownerType = "Fee Owner", leaseOnFile = false }, Json), HttpStatusCode.Created);
        await Ok<PartyLinkDto>(await designer.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/parties", new { newParty = new { kind = "Person", name = "Vested One" }, role = "FeeOwner", ownerType = "Vested Owner", leaseOnFile = false }, Json), HttpStatusCode.Created);
        await Ok<PartyLinkDto>(await designer.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/parties", new { newParty = new { kind = "Person", name = "Vested Two" }, role = "FeeOwner", ownerType = "Vested Owner", leaseOnFile = false }, Json), HttpStatusCode.Created);
        var neg = await _api.As("Negotiator");

        var saved = await Ok<TitleClosingDto>(await neg.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/title-closing", Body(), Json));

        saved.VestedOwnerName.Should().Be("Vested One; Vested Two");
        (await Ok<Envelope>(await neg.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/title-closing"))).Record!.VestedOwnerName.Should().Be("Vested One; Vested Two");
    }

    [PgFact]
    public async Task AT_TE_41_Ten_first_saves_at_once_leave_exactly_one_record()
    {
        var parcel = await NewParcel();
        var tr = await _api.As("TitleResearcher");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => tr.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/title-closing", Body(new() { ["gfNumber"] = $"GF-{i}" }), Json)));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
        results.Should().Contain(r => r.StatusCode == HttpStatusCode.OK);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_title_closings where parcel_id = @p", ("p", parcel.Id))).Should().Be(1);
    }

    [PgFact]
    public async Task AT_TE_41_Title_and_closing_input_is_checked()
    {
        var parcel = await NewParcel();
        var tr = await _api.As("TitleResearcher");
        var url = $"/api/v1/parcels/{parcel.Id}/title-closing";

        (await Status(tr.Http.PutAsJsonAsync(url, Body(new() { ["psaAmount"] = -1 }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(tr.Http.PutAsJsonAsync(url, Body(new() { ["gfNumber"] = new string('g', 101) }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(tr.Http.PutAsJsonAsync(url, Body(new() { ["settlementReceivedDate"] = "2026-04-05", ["settlementExecutedDate"] = "2026-04-01" }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(tr.Http.PutAsJsonAsync(url, Body(new() { ["closingPackageSubmittedDate"] = "2026-04-10", ["closingPackageApprovedDate"] = "2026-04-01" }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(tr.Http.PutAsJsonAsync(url, Body(new() { ["conveyanceDocuments"] = new[] { new { signedDate = "2026-04-20", recordedDate = "2026-04-01" } } }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(tr.Http.PutAsJsonAsync(url, Body(new() { ["titleCommitments"] = new[] { new { vestedOwnerName = "" } } }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(tr.Http.PutAsJsonAsync(url, Body(new() { ["titleCommitments"] = new[] { new { vestedOwnerName = "X", curativePlanSubmittedDate = "2026-02-15", curativePlanApprovedDate = "2026-02-01" } } }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(tr.Http.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/title-closing"))).Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_title_closings where parcel_id = @p", ("p", parcel.Id))).Should().Be(0, "a refused save creates nothing");
    }

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task AT_X_50_Each_role_can_do_exactly_what_the_Title_and_Closing_page_allows(string role)
    {
        var parcel = await NewParcel();
        var other = await NewParcel();
        var admin = await _api.As("Admin");
        var url = $"/api/v1/parcels/{parcel.Id}/title-closing";
        await Ok<TitleClosingDto>(await admin.Http.PutAsJsonAsync(url, Body(), Json));
        var user = await _api.As(role);
        bool Can(string action) => Spec.Can(role, "titleclosing", action);
        HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;

        (await Status(user.Http.GetAsync(url))).Should().Be(Expect(Can("read"), HttpStatusCode.OK));
        // A first save needs Create, a later one Update.
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/parcels/{other.Id}/title-closing", Body(), Json))).Should().Be(Expect(Can("create"), HttpStatusCode.OK));
        (await Status(user.Http.PutAsJsonAsync(url, Body(new() { ["gfNumber"] = $"by {role}" }), Json))).Should().Be(Expect(Can("update"), HttpStatusCode.OK));
        (await Status(user.Http.DeleteAsync(url))).Should().Be(Expect(Can("delete"), HttpStatusCode.NoContent));
    }

    public static IEnumerable<object[]> Roles() => Spec.Roles.Select(r => new object[] { r });

    [PgFact]
    public async Task Without_signing_in_the_title_and_closing_record_is_not_available()
    {
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/title-closing")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
