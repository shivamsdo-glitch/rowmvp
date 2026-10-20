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

/// <summary>Phase 1.5 slice 5c (ADR 0017): a parcel's Ownership Details and the derived Owner name, on the real API and real Postgres. Matches AT-DE-42, AT-X-48.</summary>
public class OwnershipApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly PgApi _api;
    public OwnershipApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> t) => (await t).StatusCode;

    private async Task<ParcelDto> NewParcel()
    {
        var admin = await _api.As("Admin");
        var project = await Ok<ProjectDto>(await admin.Http.PostAsJsonAsync("/api/v1/projects", new { name = $"Owners {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local" }, Json), HttpStatusCode.Created);
        return await Ok<ParcelDto>(await admin.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 100m, acquisitionType = "Standard" }, Json), HttpStatusCode.Created);
    }

    private static object Owner(string name, object[]? addresses = null, object[]? contacts = null, string? type = "Fee Owner") =>
        new { ownerName = name, ownerType = type, addresses, contacts };

    private static object Address(string city = "Austin") => new { addressType = "Tax Card Mailing Address", address = "1 Main St", contactPerson = "Kamali, Devanshi", city, state = "TX", zip = "78701" };
    private static object Contact(string name, bool primary = false, bool active = true) =>
        new { contactType = "Broker", name, phone = "(112) 345-6789", email = "kamali@example.com", address = "12 VOC Street", isActive = active, isPrimary = primary };

    [PgFact]
    public async Task AT_DE_42_A_designer_records_ownerships_with_addresses_and_contacts_and_the_parcel_shows_its_owner_name()
    {
        var parcel = await NewParcel();
        var designer = await _api.As("Designer");
        var url = $"/api/v1/parcels/{parcel.Id}/ownerships";

        var first = await Ok<OwnershipDto>(await designer.Http.PostAsJsonAsync(url, Owner("  Kamali ", new[] { Address() }, new[] { Contact("Dev Kamali", primary: true), Contact("Asha") }), Json), HttpStatusCode.Created);
        first.OwnershipNumber.Should().Be(1);
        first.ParcelNumber.Should().Be(parcel.Number);
        first.OwnerName.Should().Be("Kamali");
        first.Addresses.Should().ContainSingle().Which.City.Should().Be("Austin");
        first.Contacts.Select(c => c.Name).Should().Equal("Dev Kamali", "Asha");

        var second = await Ok<OwnershipDto>(await designer.Http.PostAsJsonAsync(url, Owner("Okafor Holdings", type: "Vested Owner"), Json), HttpStatusCode.Created);
        second.OwnershipNumber.Should().Be(2);

        (await Ok<ParcelDto>(await designer.Http.GetAsync($"/api/v1/parcels/{parcel.Id}"))).OwnerName.Should().Be("Kamali; Okafor Holdings");

        var changed = await Ok<OwnershipDto>(await designer.Http.PutAsJsonAsync($"{url}/{first.Id}", Owner("Kamali Family Trust", new[] { Address("Dallas"), Address("Houston") }, Array.Empty<object>()), Json));
        changed.OwnershipNumber.Should().Be(1, "the number never changes");
        changed.Addresses.Select(a => a.City).Should().BeEquivalentTo("Dallas", "Houston");
        changed.Contacts.Should().BeEmpty("an update replaces the contacts");
        (await Ok<ParcelDto>(await designer.Http.GetAsync($"/api/v1/parcels/{parcel.Id}"))).OwnerName.Should().Be("Kamali Family Trust; Okafor Holdings");

        (await Status(designer.Http.DeleteAsync($"{url}/{second.Id}"))).Should().Be(HttpStatusCode.NoContent);
        (await Ok<ParcelDto>(await designer.Http.GetAsync($"/api/v1/parcels/{parcel.Id}"))).OwnerName.Should().Be("Kamali Family Trust");
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_ownership_addresses a join parcel_ownerships o on o.id = a.ownership_id where o.parcel_id = @p", ("p", parcel.Id))).Should().Be(2);
        var third = await Ok<OwnershipDto>(await designer.Http.PostAsJsonAsync(url, Owner("Third"), Json), HttpStatusCode.Created);
        third.OwnershipNumber.Should().Be(2, "numbering is one more than the highest on the parcel");
    }

    [PgFact]
    public async Task AT_DE_42_A_parcel_with_no_ownerships_has_no_owner_name_and_ten_added_at_once_are_numbered_apart()
    {
        var parcel = await NewParcel();
        var designer = await _api.As("Designer");
        (await Ok<ParcelDto>(await designer.Http.GetAsync($"/api/v1/parcels/{parcel.Id}"))).OwnerName.Should().BeNull();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => designer.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/ownerships", Owner($"Owner {i}"), Json)));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(distinct sequence) from parcel_ownerships where parcel_id = @p", ("p", parcel.Id))).Should().Be(10);
    }

    [PgFact]
    public async Task AT_DE_42_Ownership_input_is_checked()
    {
        var parcel = await NewParcel();
        var designer = await _api.As("Designer");
        var url = $"/api/v1/parcels/{parcel.Id}/ownerships";

        (await Status(designer.Http.PostAsJsonAsync(url, Owner(""), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(designer.Http.PostAsJsonAsync(url, Owner(new string('n', 201)), Json))).Should().Be(HttpStatusCode.BadRequest);
        var twoPrimary = await designer.Http.PostAsJsonAsync(url, Owner("X", contacts: new[] { Contact("A", true), Contact("B", true) }), Json);
        twoPrimary.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await twoPrimary.Content.ReadAsStringAsync()).Should().Contain("Only one contact can be the primary contact.");
        (await Status(designer.Http.PostAsJsonAsync(url, Owner("X", contacts: new[] { Contact("") }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(designer.Http.PostAsJsonAsync(url, Owner("X", contacts: new object[] { new { name = "A", email = "not-an-email" } }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(designer.Http.PostAsJsonAsync(url, Owner("X", contacts: new object[] { new { name = "A", phone = "abc" } }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(designer.Http.PostAsJsonAsync(url, Owner("X", addresses: new object[] { new { zip = new string('9', 21) } }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(designer.Http.PutAsJsonAsync($"{url}/{Guid.NewGuid()}", Owner("X"), Json))).Should().Be(HttpStatusCode.NotFound);
        (await Status(designer.Http.DeleteAsync($"{url}/{Guid.NewGuid()}"))).Should().Be(HttpStatusCode.NotFound);
        (await Status(designer.Http.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/ownerships"))).Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_ownerships where parcel_id = @p", ("p", parcel.Id))).Should().Be(0, "a refused request saves nothing");
    }

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task AT_X_48_Each_role_can_do_exactly_what_the_Ownership_page_allows(string role)
    {
        var parcel = await NewParcel();
        var admin = await _api.As("Admin");
        var url = $"/api/v1/parcels/{parcel.Id}/ownerships";
        var seed = await Ok<OwnershipDto>(await admin.Http.PostAsJsonAsync(url, Owner("Seed"), Json), HttpStatusCode.Created);
        var user = await _api.As(role);
        bool Can(string action) => Spec.Can(role, "ownership", action);
        HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;

        (await Status(user.Http.GetAsync(url))).Should().Be(Expect(Can("read"), HttpStatusCode.OK));
        (await Status(user.Http.PostAsJsonAsync(url, Owner($"By {role}"), Json))).Should().Be(Expect(Can("create"), HttpStatusCode.Created));
        (await Status(user.Http.PutAsJsonAsync($"{url}/{seed.Id}", Owner("Seed", type: $"edited by {role}"), Json))).Should().Be(Expect(Can("update"), HttpStatusCode.OK));
        (await Status(user.Http.DeleteAsync($"{url}/{seed.Id}"))).Should().Be(Expect(Can("delete"), HttpStatusCode.NoContent));
    }

    public static IEnumerable<object[]> Roles() => Spec.Roles.Select(r => new object[] { r });

    [PgFact]
    public async Task Without_signing_in_ownerships_are_not_available()
    {
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/ownerships")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
