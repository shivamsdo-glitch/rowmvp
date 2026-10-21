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

/// <summary>Phase 1.5 slice 7a (ADR 0019): a parcel's Negotiation record on the real API and real Postgres. Matches AT-NE-42, AT-X-52.</summary>
public class NegotiationApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly PgApi _api;
    public NegotiationApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private sealed record Envelope(NegotiationDto? Record);

    private static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> t) => (await t).StatusCode;

    private async Task<ParcelDto> NewParcel()
    {
        var admin = await _api.As("Admin");
        var project = await Ok<ProjectDto>(await admin.Http.PostAsJsonAsync("/api/v1/projects", new { name = $"NG {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local" }, Json), HttpStatusCode.Created);
        return await Ok<ParcelDto>(await admin.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 100m, acquisitionType = "Standard" }, Json), HttpStatusCode.Created);
    }

    private static Dictionary<string, object?> Body(Dictionary<string, object?>? extra = null)
    {
        var b = new Dictionary<string, object?>
        {
            ["assignedTo"] = "Casey Closer", ["totalAgreementAmount"] = 250000.75m, ["notes"] = "Owner is open to a deal.",
            ["counterofferSubmittedToJatDate"] = "2026-06-01", ["counterofferResponseFromJatDate"] = "2026-06-10", ["jatResponse"] = "Approved", ["ftaApprovalRequiredForCounteroffer"] = true,
            ["puaRequestedDate"] = "2026-07-01", ["puaApprovedDate"] = "2026-07-05", ["puaApprovedAmount"] = 5000m, ["puaExecutedDate"] = "2026-07-10", ["puaClosedDate"] = "2026-07-20",
            ["statusNotes"] = new[] { new { noteDate = "2026-05-01", text = "Letter sent." }, new { noteDate = "2026-05-10", text = "Owner called back." } },
            ["offers"] = new[] { new { offerType = "Initial Offer", mailedDate = "2026-05-15", deliveredDate = "2026-05-17", amount = 200000m, notes = "First offer." } },
            ["settlements"] = new[] { new { settlementType = "Administrative", counterofferAmount = 260000m, dateReceived = "2026-05-25", dateSubmitted = "2026-05-26", counterofferResponse = "Accepted", responseDateFromAtp = "2026-05-30", responseDateToOwner = "2026-06-01", settlementNotes = "Agreed." } },
        };
        if (extra is not null) foreach (var kv in extra) b[kv.Key] = kv.Value;
        return b;
    }

    [PgFact]
    public async Task AT_NE_42_A_negotiator_saves_changes_and_removes_the_one_record_of_a_parcel()
    {
        var parcel = await NewParcel();
        var ng = await _api.As("Negotiator");
        var url = $"/api/v1/parcels/{parcel.Id}/negotiation";

        (await Ok<Envelope>(await ng.Http.GetAsync(url))).Record.Should().BeNull("nothing has been saved yet");

        var saved = await Ok<NegotiationDto>(await ng.Http.PutAsJsonAsync(url, Body(), Json));
        saved.Number.Should().Be($"NEG-{parcel.Number}");
        saved.TotalAgreementAmount.Should().Be(250000.75m);
        saved.FtaApprovalRequiredForCounteroffer.Should().BeTrue();
        saved.StatusNotes.Select(n => n.Text).Should().Equal("Letter sent.", "Owner called back.");
        saved.Offers.Should().ContainSingle().Which.Amount.Should().Be(200000m);
        saved.Settlements.Should().ContainSingle().Which.CounterofferResponse.Should().Be("Accepted");

        var changed = await Ok<NegotiationDto>(await ng.Http.PutAsJsonAsync(url, Body(new() { ["jatResponse"] = "Rejected", ["offers"] = Array.Empty<object>(), ["puaClosedDate"] = null }), Json));
        changed.Id.Should().Be(saved.Id, "a second save changes the same record");
        changed.JatResponse.Should().Be("Rejected");
        changed.Offers.Should().BeEmpty("a save replaces the grids as sent");
        changed.Settlements.Should().HaveCount(1);
        changed.PuaClosedDate.Should().BeNull();
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_negotiations where parcel_id = @p", ("p", parcel.Id))).Should().Be(1);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from negotiation_offers o join parcel_negotiations n on n.id = o.negotiation_id where n.parcel_id = @p", ("p", parcel.Id))).Should().Be(0);

        var admin = await _api.As("Admin");
        (await Status(admin.Http.DeleteAsync(url))).Should().Be(HttpStatusCode.NoContent);
        (await Ok<Envelope>(await admin.Http.GetAsync(url))).Record.Should().BeNull();
        (await Status(admin.Http.DeleteAsync(url))).Should().Be(HttpStatusCode.NotFound);
    }

    [PgFact]
    public async Task AT_NE_42_Ten_first_saves_at_once_leave_exactly_one_record()
    {
        var parcel = await NewParcel();
        var ng = await _api.As("Negotiator");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => ng.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/negotiation", Body(new() { ["notes"] = $"n{i}" }), Json)));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
        results.Should().Contain(r => r.StatusCode == HttpStatusCode.OK);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_negotiations where parcel_id = @p", ("p", parcel.Id))).Should().Be(1);
    }

    [PgFact]
    public async Task AT_NE_42_Negotiation_input_is_checked()
    {
        var parcel = await NewParcel();
        var ng = await _api.As("Negotiator");
        var url = $"/api/v1/parcels/{parcel.Id}/negotiation";
        Task<HttpStatusCode> Put(string key, object? value) => Status(ng.Http.PutAsJsonAsync(url, Body(new() { [key] = value }), Json));

        (await Put("totalAgreementAmount", -1)).Should().Be(HttpStatusCode.BadRequest);
        (await Put("puaApprovedAmount", -1)).Should().Be(HttpStatusCode.BadRequest);
        (await Put("notes", new string('n', 4001))).Should().Be(HttpStatusCode.BadRequest);
        (await Put("counterofferResponseFromJatDate", "2026-05-01")).Should().Be(HttpStatusCode.BadRequest);
        (await Put("puaApprovedDate", "2026-06-01")).Should().Be(HttpStatusCode.BadRequest);
        (await Put("puaClosedDate", "2026-07-01")).Should().Be(HttpStatusCode.BadRequest);
        (await Put("statusNotes", new[] { new { noteDate = "2026-05-01", text = "" } })).Should().Be(HttpStatusCode.BadRequest);
        (await Put("offers", new[] { new { amount = -5m } })).Should().Be(HttpStatusCode.BadRequest);
        (await Put("offers", new[] { new { mailedDate = "2026-05-05", deliveredDate = "2026-05-01" } })).Should().Be(HttpStatusCode.BadRequest);
        (await Put("settlements", new[] { new { counterofferAmount = -5m } })).Should().Be(HttpStatusCode.BadRequest);
        (await Put("settlements", new[] { new { dateReceived = "2026-05-05", dateSubmitted = "2026-05-01" } })).Should().Be(HttpStatusCode.BadRequest);
        (await Put("offers", Enumerable.Range(0, 51).Select(_ => new { amount = 1m }).ToArray())).Should().Be(HttpStatusCode.BadRequest);
        (await Status(ng.Http.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/negotiation"))).Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_negotiations where parcel_id = @p", ("p", parcel.Id))).Should().Be(0, "a refused save creates nothing");
    }

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task AT_X_52_Each_role_can_do_exactly_what_the_Negotiation_page_allows(string role)
    {
        var parcel = await NewParcel();
        var other = await NewParcel();
        var admin = await _api.As("Admin");
        var url = $"/api/v1/parcels/{parcel.Id}/negotiation";
        await Ok<NegotiationDto>(await admin.Http.PutAsJsonAsync(url, Body(), Json));
        var user = await _api.As(role);
        bool Can(string action) => Spec.Can(role, "parcelnegotiation", action);
        HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;

        (await Status(user.Http.GetAsync(url))).Should().Be(Expect(Can("read"), HttpStatusCode.OK));
        // A first save needs Create, a later one Update.
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/parcels/{other.Id}/negotiation", Body(), Json))).Should().Be(Expect(Can("create"), HttpStatusCode.OK));
        (await Status(user.Http.PutAsJsonAsync(url, Body(new() { ["notes"] = $"by {role}" }), Json))).Should().Be(Expect(Can("update"), HttpStatusCode.OK));
        (await Status(user.Http.DeleteAsync(url))).Should().Be(Expect(Can("delete"), HttpStatusCode.NoContent));
    }

    public static IEnumerable<object[]> Roles() => Spec.Roles.Select(r => new object[] { r });

    [PgFact]
    public async Task Without_signing_in_the_negotiation_record_is_not_available()
    {
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/negotiation")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
