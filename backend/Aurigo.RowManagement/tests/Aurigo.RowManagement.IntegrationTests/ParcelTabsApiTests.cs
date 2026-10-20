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

/// <summary>
/// Phase 1.5 slice 5b (ADR 0017): the Contact Log and Easements tabs of a parcel on the real API and real Postgres.
/// Matches AT-NE-40, AT-SU-40, AT-X-46, AT-X-47.
/// </summary>
public class ParcelTabsApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly PgApi _api;
    public ParcelTabsApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> t) => (await t).StatusCode;
    private static async Task<string> Title(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString()!;

    private async Task<ParcelDto> NewParcel()
    {
        var admin = await _api.As("Admin");
        var project = await Ok<ProjectDto>(await admin.Http.PostAsJsonAsync("/api/v1/projects", new { name = $"Tabs {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local" }, Json), HttpStatusCode.Created);
        return await Ok<ParcelDto>(await admin.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 100m, acquisitionType = "Standard" }, Json), HttpStatusCode.Created);
    }

    private static object Contact(string notes = "Explained the offer.", object[]? links = null, string when = "2026-10-01T15:30:00Z") =>
        new { contactedAt = when, mode = "Call", contactedBy = "Pat Negotiator", contactPerson = "Alex Marlowe", notes, links };

    private static object Easement(string number, string? start = "2026-01-01", string? end = "2036-01-01") =>
        new { number, easementType = "Permanent Easement", description = "Sidewalk along Main", startDate = start, endDate = end, startStation = "10+00", endStation = "12+50" };

    // ── Contact Log ──

    [PgFact]
    public async Task AT_NE_40_A_negotiator_logs_changes_and_removes_contacts_that_are_numbered_by_parcel()
    {
        var parcel = await NewParcel();
        var neg = await _api.As("Negotiator");
        var url = $"/api/v1/parcels/{parcel.Id}/contact-log";

        var first = await Ok<ContactLogEntryDto>(await neg.Http.PostAsJsonAsync(url, Contact(links: new object[] { new { recordIdentifier = "APP-001", moduleName = "Appraisal", notes = "see value" } }), Json), HttpStatusCode.Created);
        first.Number.Should().Be($"CL-{parcel.Number}-001");
        first.Mode.Should().Be("Call");
        first.ContactPerson.Should().Be("Alex Marlowe");
        first.ContactedAt.Should().Be(new DateTime(2026, 10, 1, 15, 30, 0, DateTimeKind.Utc));
        first.Links.Should().ContainSingle().Which.RecordIdentifier.Should().Be("APP-001");

        var second = await Ok<ContactLogEntryDto>(await neg.Http.PostAsJsonAsync(url, Contact("Second call", when: "2026-10-02T09:00:00Z"), Json), HttpStatusCode.Created);
        second.Number.Should().Be($"CL-{parcel.Number}-002");
        (await Ok<List<ContactLogEntryDto>>(await neg.Http.GetAsync(url))).Select(e => e.Number).Should().Equal($"CL-{parcel.Number}-002", $"CL-{parcel.Number}-001");

        var changed = await Ok<ContactLogEntryDto>(await neg.Http.PutAsJsonAsync($"{url}/{first.Id}", Contact("Changed", links: Array.Empty<object>()), Json));
        changed.Notes.Should().Be("Changed");
        changed.Links.Should().BeEmpty("an update replaces the linked records");
        changed.Number.Should().Be(first.Number, "the id never changes");

        (await Status(neg.Http.DeleteAsync($"{url}/{first.Id}"))).Should().Be(HttpStatusCode.NoContent);
        var third = await Ok<ContactLogEntryDto>(await neg.Http.PostAsJsonAsync(url, Contact("Third"), Json), HttpStatusCode.Created);
        third.Number.Should().Be($"CL-{parcel.Number}-003", "removing an earlier entry never makes a number come round again");
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_type = 'ParcelContactLogEntry' and entity_id = @id and action = 'Delete'", ("id", first.Id))).Should().Be(1);
    }

    [PgFact]
    public async Task AT_NE_40_Ten_contacts_added_at_once_all_get_different_numbers()
    {
        var parcel = await NewParcel();
        var neg = await _api.As("Negotiator");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => neg.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/contact-log", Contact($"Call {i}"), Json)));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(distinct sequence) from parcel_contact_log_entries where parcel_id = @p", ("p", parcel.Id))).Should().Be(10);
    }

    [PgFact]
    public async Task AT_NE_40_Contact_log_input_is_checked_and_unknown_records_are_not_found()
    {
        var parcel = await NewParcel();
        var neg = await _api.As("Negotiator");
        var url = $"/api/v1/parcels/{parcel.Id}/contact-log";

        (await Status(neg.Http.PostAsJsonAsync(url, Contact(new string('n', 4001)), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(neg.Http.PostAsJsonAsync(url, Contact(links: new object[] { new { recordIdentifier = "", moduleName = "x" } }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(neg.Http.PostAsJsonAsync(url, Contact(when: "0001-01-01T00:00:00Z"), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(neg.Http.PutAsJsonAsync($"{url}/{Guid.NewGuid()}", Contact(), Json))).Should().Be(HttpStatusCode.NotFound);
        (await Status(neg.Http.DeleteAsync($"{url}/{Guid.NewGuid()}"))).Should().Be(HttpStatusCode.NotFound);
        (await Status(neg.Http.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/contact-log"))).Should().Be(HttpStatusCode.NotFound);
        (await Status(neg.Http.PostAsJsonAsync(url, Contact(when: "2026-10-01T15:30:00"), Json))).Should().Be(HttpStatusCode.Created, "a time without a zone is taken as UTC");
    }

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task AT_X_46_Each_role_can_do_exactly_what_the_Contact_Log_page_allows(string role)
    {
        var parcel = await NewParcel();
        var admin = await _api.As("Admin");
        var url = $"/api/v1/parcels/{parcel.Id}/contact-log";
        var seed = await Ok<ContactLogEntryDto>(await admin.Http.PostAsJsonAsync(url, Contact("seed"), Json), HttpStatusCode.Created);
        var user = await _api.As(role);
        bool Can(string action) => Spec.Can(role, "contactlog", action);
        HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;

        (await Status(user.Http.GetAsync(url))).Should().Be(Expect(Can("read"), HttpStatusCode.OK));
        (await Status(user.Http.PostAsJsonAsync(url, Contact($"by {role}"), Json))).Should().Be(Expect(Can("create"), HttpStatusCode.Created));
        (await Status(user.Http.PutAsJsonAsync($"{url}/{seed.Id}", Contact($"edited by {role}"), Json))).Should().Be(Expect(Can("update"), HttpStatusCode.OK));
        (await Status(user.Http.DeleteAsync($"{url}/{seed.Id}"))).Should().Be(Expect(Can("delete"), HttpStatusCode.NoContent));
        if (!Can("create"))
            (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_contact_log_entries where notes = @t", ("t", $"by {role}"))).Should().Be(0, "a refused request changes nothing");
    }

    // ── Easements ──

    [PgFact]
    public async Task AT_SU_40_A_surveyor_adds_changes_and_removes_easements_and_numbers_are_unique_per_parcel()
    {
        var parcel = await NewParcel();
        var other = await NewParcel();
        var sur = await _api.As("Surveyor");
        var url = $"/api/v1/parcels/{parcel.Id}/easements";

        var e = await Ok<EasementDto>(await sur.Http.PostAsJsonAsync(url, Easement("  E-1 "), Json), HttpStatusCode.Created);
        e.Number.Should().Be("E-1");
        e.StartDate.Should().Be(new DateOnly(2026, 1, 1));
        e.StartStation.Should().Be("10+00");

        var dup = await sur.Http.PostAsJsonAsync(url, Easement("E-1"), Json);
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Title(dup)).Should().Be("easement_number_exists");
        (await Status(sur.Http.PostAsJsonAsync($"/api/v1/parcels/{other.Id}/easements", Easement("E-1"), Json))).Should().Be(HttpStatusCode.Created, "another parcel may use the same number");

        await Ok<EasementDto>(await sur.Http.PostAsJsonAsync(url, Easement("E-2"), Json), HttpStatusCode.Created);
        (await Status(sur.Http.PutAsJsonAsync($"{url}/{e.Id}", Easement("E-2"), Json))).Should().Be(HttpStatusCode.Conflict);
        var changed = await Ok<EasementDto>(await sur.Http.PutAsJsonAsync($"{url}/{e.Id}", Easement("E-1", end: null), Json));
        changed.EndDate.Should().BeNull();
        (await Ok<List<EasementDto>>(await sur.Http.GetAsync(url))).Select(x => x.Number).Should().Equal("E-1", "E-2");

        (await Status(sur.Http.DeleteAsync($"{url}/{e.Id}"))).Should().Be(HttpStatusCode.NoContent);
        (await Ok<List<EasementDto>>(await sur.Http.GetAsync(url))).Should().ContainSingle().Which.Number.Should().Be("E-2");
    }

    [PgFact]
    public async Task AT_SU_40_Easement_input_is_checked()
    {
        var parcel = await NewParcel();
        var sur = await _api.As("Surveyor");
        var url = $"/api/v1/parcels/{parcel.Id}/easements";

        (await Status(sur.Http.PostAsJsonAsync(url, Easement(""), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(sur.Http.PostAsJsonAsync(url, Easement(new string('x', 51)), Json))).Should().Be(HttpStatusCode.BadRequest);
        var backwards = await sur.Http.PostAsJsonAsync(url, Easement("E-9", "2026-05-01", "2026-04-01"), Json);
        backwards.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await backwards.Content.ReadAsStringAsync()).Should().Contain("The end date cannot be before the start date.");
        (await Status(sur.Http.PostAsJsonAsync(url, Easement("E-9", "2026-05-01", "2026-05-01"), Json))).Should().Be(HttpStatusCode.Created, "the same day is allowed");
        (await Status(sur.Http.PutAsJsonAsync($"{url}/{Guid.NewGuid()}", Easement("E-10"), Json))).Should().Be(HttpStatusCode.NotFound);
    }

    [PgFact]
    public async Task AT_SU_40_Ten_easements_with_the_same_number_at_once_produce_exactly_one()
    {
        var parcel = await NewParcel();
        var sur = await _api.As("Surveyor");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => sur.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/easements", Easement("RACE"), Json)));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Where(r => r.StatusCode != HttpStatusCode.Created).Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Conflict);
    }

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task AT_X_47_Each_role_can_do_exactly_what_the_Easements_page_allows(string role)
    {
        var parcel = await NewParcel();
        var admin = await _api.As("Admin");
        var url = $"/api/v1/parcels/{parcel.Id}/easements";
        var seed = await Ok<EasementDto>(await admin.Http.PostAsJsonAsync(url, Easement("SEED"), Json), HttpStatusCode.Created);
        var user = await _api.As(role);
        bool Can(string action) => Spec.Can(role, "easements", action);
        HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;

        (await Status(user.Http.GetAsync(url))).Should().Be(Expect(Can("read"), HttpStatusCode.OK));
        (await Status(user.Http.PostAsJsonAsync(url, Easement($"BY-{role}"), Json))).Should().Be(Expect(Can("create"), HttpStatusCode.Created));
        (await Status(user.Http.PutAsJsonAsync($"{url}/{seed.Id}", Easement("SEED", end: "2040-01-01"), Json))).Should().Be(Expect(Can("update"), HttpStatusCode.OK));
        (await Status(user.Http.DeleteAsync($"{url}/{seed.Id}"))).Should().Be(Expect(Can("delete"), HttpStatusCode.NoContent));
    }

    public static IEnumerable<object[]> Roles() => Spec.Roles.Select(r => new object[] { r });

    [PgFact]
    public async Task Without_signing_in_neither_tab_is_available()
    {
        var anon = _api.Factory.CreateClient();
        foreach (var part in new[] { "contact-log", "easements" })
            (await anon.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/{part}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
