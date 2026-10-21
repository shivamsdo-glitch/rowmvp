using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Parties;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>
/// The Ownership interests tab (ADR 0023: Ownership Details and Parties combined): a parcel's owners with their owner type, addresses and
/// contact people, and the derived Owner name, on the real API and real Postgres. Matches AT-DE-42, AT-X-48.
/// </summary>
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

    private static object NewOwner(string name, object[]? addresses = null, object[]? contacts = null, string? type = "Fee Owner") => new
    {
        newParty = new { kind = "Person", name, addresses, contacts },
        role = "FeeOwner", ownerType = type, leaseOnFile = false,
    };

    private static object Address(string city = "Austin") => new { addressType = "Tax Card Mailing Address", address = "1 Main St", contactPerson = "Kamali, Devanshi", city, state = "TX", zip = "78701" };
    private static object Contact(string name, bool primary = false, bool active = true) =>
        new { contactType = "Broker", name, phone = "(112) 345-6789", email = "kamali@example.com", address = "12 VOC Street", isActive = active, isPrimary = primary };

    private static object PartyBody(string name, object[]? addresses, object[]? contacts) =>
        new { kind = "Person", name, addresses, contacts };

    [PgFact]
    public async Task AT_DE_42_A_designer_records_owners_with_addresses_and_contacts_and_the_parcel_shows_its_owner_name()
    {
        var parcel = await NewParcel();
        var designer = await _api.As("Designer");
        var url = $"/api/v1/parcels/{parcel.Id}/parties";

        var first = await Ok<PartyLinkDto>(await designer.Http.PostAsJsonAsync(url, NewOwner("  Kamali ", new[] { Address() }, new[] { Contact("Dev Kamali", primary: true), Contact("Asha") }), Json), HttpStatusCode.Created);
        first.Party.Name.Should().Be("Kamali");
        first.OwnerType.Should().Be("Fee Owner");

        var party = await Ok<PartyDto>(await designer.Http.GetAsync($"/api/v1/parties/{first.Party.Id}"));
        party.Addresses.Should().ContainSingle().Which.City.Should().Be("Austin");
        party.Contacts.Select(c => c.Name).Should().Equal("Dev Kamali", "Asha");

        await Ok<PartyLinkDto>(await designer.Http.PostAsJsonAsync(url, NewOwner("Okafor Holdings", type: "Vested Owner"), Json), HttpStatusCode.Created);
        (await Ok<ParcelDto>(await designer.Http.GetAsync($"/api/v1/parcels/{parcel.Id}"))).OwnerName.Should().Be("Kamali; Okafor Holdings");

        var changed = await Ok<PartyDto>(await designer.Http.PutAsJsonAsync($"/api/v1/parties/{first.Party.Id}", PartyBody("Kamali Family Trust", new[] { Address("Dallas"), Address("Houston") }, Array.Empty<object>()), Json));
        changed.Addresses.Select(a => a.City).Should().BeEquivalentTo("Dallas", "Houston");
        changed.Contacts.Should().BeEmpty("an update replaces the contacts");
        (await Ok<ParcelDto>(await designer.Http.GetAsync($"/api/v1/parcels/{parcel.Id}"))).OwnerName.Should().Be("Kamali Family Trust; Okafor Holdings");

        var typed = await Ok<PartyLinkDto>(await designer.Http.PutAsJsonAsync($"/api/v1/party-links/{first.Id}", new { ownerType = "Prior Owner", leaseOnFile = false }, Json));
        typed.OwnerType.Should().Be("Prior Owner");

        (await Status(designer.Http.DeleteAsync($"/api/v1/party-links/{first.Id}"))).Should().Be(HttpStatusCode.NoContent);
        (await Ok<ParcelDto>(await designer.Http.GetAsync($"/api/v1/parcels/{parcel.Id}"))).OwnerName.Should().Be("Okafor Holdings");
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from party_addresses where party_id = @p", ("p", first.Party.Id))).Should().Be(2, "the party keeps its addresses when unlinked");
    }

    [PgFact]
    public async Task AT_DE_42_A_party_edit_without_addresses_or_contacts_leaves_the_saved_ones_alone()
    {
        var parcel = await NewParcel();
        var designer = await _api.As("Designer");
        var link = await Ok<PartyLinkDto>(await designer.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/parties", NewOwner("Keeper", new[] { Address() }, new[] { Contact("Pat") }), Json), HttpStatusCode.Created);

        var renamed = await Ok<PartyDto>(await designer.Http.PutAsJsonAsync($"/api/v1/parties/{link.Party.Id}", new { kind = "Person", name = "Keeper Renamed" }, Json));

        renamed.Name.Should().Be("Keeper Renamed");
        renamed.Addresses.Should().HaveCount(1);
        renamed.Contacts.Should().HaveCount(1);
    }

    [PgFact]
    public async Task AT_DE_42_A_parcel_with_no_owners_has_no_owner_name_and_owners_added_at_the_same_moment_never_overlap()
    {
        var parcel = await NewParcel();
        var designer = await _api.As("Designer");
        (await Ok<ParcelDto>(await designer.Http.GetAsync($"/api/v1/parcels/{parcel.Id}"))).OwnerName.Should().BeNull();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => designer.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/parties", NewOwner($"Owner {i}"), Json)));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.Conflict, "owners hold shares, so two saved at once cannot both win");
        var created = results.Count(r => r.StatusCode == HttpStatusCode.Created);
        created.Should().BeGreaterThan(0);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from party_links where parcel_id = @p", ("p", parcel.Id))).Should().Be(created);
    }

    [PgFact]
    public async Task AT_DE_42_Owner_input_is_checked()
    {
        var parcel = await NewParcel();
        var designer = await _api.As("Designer");
        var url = $"/api/v1/parcels/{parcel.Id}/parties";

        (await Status(designer.Http.PostAsJsonAsync(url, NewOwner(""), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(designer.Http.PostAsJsonAsync(url, NewOwner(new string('n', 201)), Json))).Should().Be(HttpStatusCode.BadRequest);
        var twoPrimary = await designer.Http.PostAsJsonAsync(url, NewOwner("X", contacts: new[] { Contact("A", true), Contact("B", true) }), Json);
        twoPrimary.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await twoPrimary.Content.ReadAsStringAsync()).Should().Contain("Only one contact can be the primary contact.");
        (await Status(designer.Http.PostAsJsonAsync(url, NewOwner("X", contacts: new[] { Contact("") }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(designer.Http.PostAsJsonAsync(url, NewOwner("X", contacts: new object[] { new { name = "A", email = "not-an-email" } }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(designer.Http.PostAsJsonAsync(url, NewOwner("X", contacts: new object[] { new { name = "A", phone = "abc" } }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(designer.Http.PostAsJsonAsync(url, NewOwner("X", addresses: new object[] { new { zip = new string('9', 21) } }), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(designer.Http.PostAsJsonAsync(url, NewOwner("X", type: new string('t', 101)), Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(designer.Http.PostAsJsonAsync(url, new { newParty = new { kind = "Person", name = "Lawyer" }, role = "Attorney", ownerType = "Vested Owner", leaseOnFile = false }, Json)))
            .Should().Be(HttpStatusCode.BadRequest, "an owner type applies to ownership interests only");
        (await Status(designer.Http.PutAsJsonAsync($"/api/v1/party-links/{Guid.NewGuid()}", new { leaseOnFile = false }, Json))).Should().Be(HttpStatusCode.NotFound);
        (await Status(designer.Http.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/parties"))).Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from party_links where parcel_id = @p", ("p", parcel.Id))).Should().Be(0, "a refused request saves nothing");
    }

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task AT_X_48_Each_role_can_do_exactly_what_the_Parties_page_allows_on_the_Ownership_interests_tab(string role)
    {
        var parcel = await NewParcel();
        var admin = await _api.As("Admin");
        var url = $"/api/v1/parcels/{parcel.Id}/parties";
        var seed = await Ok<PartyLinkDto>(await admin.Http.PostAsJsonAsync(url, NewOwner("Seed"), Json), HttpStatusCode.Created);
        var user = await _api.As(role);
        bool Can(string action) => Spec.Can(role, "parties", action);
        HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;

        (await Status(user.Http.GetAsync(url))).Should().Be(Expect(Can("read"), HttpStatusCode.OK));
        (await Status(user.Http.PostAsJsonAsync(url, NewOwner($"By {role}"), Json))).Should().Be(Expect(Can("create"), HttpStatusCode.Created));
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/party-links/{seed.Id}", new { ownerType = $"edited by {role}", leaseOnFile = false }, Json))).Should().Be(Expect(Can("update"), HttpStatusCode.OK));
        (await Status(user.Http.DeleteAsync($"/api/v1/party-links/{seed.Id}"))).Should().Be(Expect(Can("delete"), HttpStatusCode.NoContent));
    }

    public static IEnumerable<object[]> Roles() => Spec.Roles.Select(r => new object[] { r });

    [PgFact]
    public async Task Without_signing_in_owners_are_not_available()
    {
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/parties")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
