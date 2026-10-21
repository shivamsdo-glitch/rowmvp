using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Parties;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>
/// Owners, interests, contacts, tenants, abutters and W-9 tax information on the real API and real Postgres
/// (ROW-014..018): the job, every role in both directions, races, tenancy, audit, schema and hostile input.
/// Matches acceptance cases AT-PM-22 and AT-EV-PARTIES-*.
/// </summary>
public class PartyApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Guid OtherTenant = Guid.Parse("00000000-0000-0000-0000-0000000000e5");
    private static readonly string[] AllRoles = Aurigo.RowManagement.IntegrationTests.Support.Spec.Roles;

    private readonly PgApi _api;
    public PartyApiTests(PgApi api) => _api = api;

    // ── helpers ──

    private static async Task<T> Read<T>(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<T>(Json))!;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return await Read<T>(r);
    }

    private static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> t) => (await t).StatusCode;

    private async Task<ProjectDto> NewProject(TestUser user)
    {
        var res = await user.Http.PostAsJsonAsync("/api/v1/projects", new { name = $"Party project {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local" }, Json);
        return await Ok<ProjectDto>(res, HttpStatusCode.Created);
    }

    private static async Task<ParentTractDto> NewTract(TestUser user, Guid projectId, string number = "T-1") =>
        await Ok<ParentTractDto>(await user.Http.PostAsJsonAsync($"/api/v1/projects/{projectId}/tracts", new { number, description = "Home place" }, Json), HttpStatusCode.Created);

    private static async Task<ParcelDto> NewParcel(TestUser user, Guid projectId, Guid? tractId = null, decimal area = 10_000m)
    {
        var res = await user.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId, parentTractId = tractId, areaSqFt = area, acquisitionType = "Standard" }, Json);
        return await Ok<ParcelDto>(res, HttpStatusCode.Created);
    }

    private static object PartyBody(string name = "Pat Owner", string kind = "Person") => new
    {
        kind, name, email = "pat@example.com", phone = "555-123-4567", addressLine1 = "1 Elm St", city = "Springfield", state = "IL", postalCode = "62701",
    };

    private static async Task<PartyDto> NewParty(TestUser user, string? name = null) =>
        await Ok<PartyDto>(await user.Http.PostAsJsonAsync("/api/v1/parties", PartyBody(name ?? $"Party {Guid.NewGuid().ToString("N")[..8]}"), Json), HttpStatusCode.Created);

    private static object Link(Guid partyId, string role = "FeeOwner", decimal? share = null) =>
        new { partyId, role, sharePercent = share, leaseOnFile = false };

    private static object NewPersonLink(string name, string role = "FeeOwner", decimal? share = null) =>
        new { newParty = PartyBody(name), role, sharePercent = share, leaseOnFile = false };

    private static Task<HttpResponseMessage> AddToTract(TestUser user, Guid projectId, Guid tractId, object link) =>
        user.Http.PostAsJsonAsync($"/api/v1/projects/{projectId}/tracts/{tractId}/parties", link, Json);

    private static Task<HttpResponseMessage> AddToParcel(TestUser user, Guid parcelId, object link) =>
        user.Http.PostAsJsonAsync($"/api/v1/parcels/{parcelId}/parties", link, Json);

    private async Task EnsureOtherTenantAsync()
    {
        using var scope = _api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();
        if (!await db.Tenants.AnyAsync(t => t.Id == OtherTenant))
        {
            db.Tenants.Add(new Tenant { Id = OtherTenant, Name = "Other Agency E5", Slug = "other-e5", IsActive = true });
            await db.SaveChangesAsync();
        }
        await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().EnsureDefaultRolesAsync(OtherTenant);
    }

    // ── the job (AT-PM-22) ──

    [PgFact]
    public async Task AT_PM_22_ROW_014_to_017_A_project_manager_records_who_owns_lives_on_and_borders_a_parcel()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var tract = await NewTract(pm, project.Id);
        var parcel = await NewParcel(pm, project.Id, tract.Id);
        var existing = await NewParty(pm, "Existing Co-owner");

        var fee = await Ok<PartyLinkDto>(await AddToTract(pm, project.Id, tract.Id, NewPersonLink("Pat Owner", "FeeOwner", 60m)), HttpStatusCode.Created);
        await Ok<PartyLinkDto>(await AddToTract(pm, project.Id, tract.Id, Link(existing.Id, "FeeOwner", 40m)), HttpStatusCode.Created);
        await Ok<PartyLinkDto>(await AddToTract(pm, project.Id, tract.Id, NewPersonLink("First Bank", "Lienholder")), HttpStatusCode.Created);
        await Ok<PartyLinkDto>(await AddToParcel(pm, parcel.Id, NewPersonLink("Lee Lawyer", "Attorney")), HttpStatusCode.Created);
        var tenant = await Ok<PartyLinkDto>(await AddToParcel(pm, parcel.Id,
            new { newParty = PartyBody("Tina Tenant"), role = "Tenant", leaseOnFile = true, leaseDetails = "Month to month", insuranceDetails = "Renter policy 55" }), HttpStatusCode.Created);
        await Ok<PartyLinkDto>(await AddToParcel(pm, parcel.Id, NewPersonLink("Abe Abutter", "Abutter")), HttpStatusCode.Created);

        fee.Source.Should().Be("Tract");
        fee.TractNumber.Should().Be("T-1");
        fee.Party.State.Should().Be("IL");
        tenant.LeaseOnFile.Should().BeTrue();

        var seen = await Ok<ParcelPartiesDto>(await pm.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/parties"));
        seen.Ownership.Select(l => (l.Party.Name, l.Role, l.SharePercent, l.Source)).Should().BeEquivalentTo(new[]
        {
            ("Pat Owner", PartyRole.FeeOwner, (decimal?)60m, "Tract"),
            ("Existing Co-owner", PartyRole.FeeOwner, (decimal?)40m, "Tract"),
            ("First Bank", PartyRole.Lienholder, (decimal?)null, "Tract"),
        });
        seen.Contacts.Should().ContainSingle().Which.Party.Name.Should().Be("Lee Lawyer");
        seen.Occupants.Should().ContainSingle().Which.InsuranceDetails.Should().Be("Renter policy 55");
        seen.Abutters.Should().ContainSingle().Which.Party.Name.Should().Be("Abe Abutter");
        seen.TractNumber.Should().Be("T-1");

        var onTract = await Ok<ParcelPartiesDto>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}/tracts/{tract.Id}/parties"));
        onTract.Ownership.Should().HaveCount(3);
        onTract.Contacts.Should().BeEmpty("the attorney was recorded on the parcel");

        var usage = await Ok<List<PartyUsageDto>>(await pm.Http.GetAsync($"/api/v1/parties/{existing.Id}/usage"));
        usage.Should().ContainSingle().Which.TargetLabel.Should().Be("Tract T-1");
        (await Ok<PartyDto>(await pm.Http.GetAsync($"/api/v1/parties/{existing.Id}"))).LinkCount.Should().Be(1);
    }

    [PgFact]
    public async Task AT_PM_22_ROW_014_Owners_belong_on_the_tract_and_shares_cannot_pass_one_hundred_percent()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var tract = await NewTract(pm, project.Id);
        var onTract = await NewParcel(pm, project.Id, tract.Id);
        var loose = await NewParcel(pm, project.Id);

        var wrongPlace = await AddToParcel(pm, onTract.Id, NewPersonLink("Pat Owner", "FeeOwner", 100m));
        wrongPlace.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrongPlace.Content.ReadAsStringAsync()).Should().Contain("owners_on_tract").And.Contain("T-1");
        await Ok<PartyLinkDto>(await AddToParcel(pm, loose.Id, NewPersonLink("Pat Owner", "FeeOwner", 100m)), HttpStatusCode.Created);

        await Ok<PartyLinkDto>(await AddToTract(pm, project.Id, tract.Id, NewPersonLink("A", "FeeOwner", 70m)), HttpStatusCode.Created);
        var over = await AddToTract(pm, project.Id, tract.Id, NewPersonLink("B", "FeeOwner", 30.5m));
        over.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await over.Content.ReadAsStringAsync()).Should().Contain("share_exceeds_whole").And.Contain("30 percent is unclaimed");
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parties where name = 'B' and tenant_id = @t", ("t", pm.TenantId))).Should().Be(0,
            "a refused link must not leave a stray party behind");
        await Ok<PartyLinkDto>(await AddToTract(pm, project.Id, tract.Id, NewPersonLink("B", "FeeOwner", 30m)), HttpStatusCode.Created);
        await Ok<PartyLinkDto>(await AddToTract(pm, project.Id, tract.Id, NewPersonLink("Utility", "EasementHolder", 100m)), HttpStatusCode.Created);
    }

    [PgFact]
    public async Task AT_PM_22_ROW_014_A_link_can_be_edited_and_removed_and_a_party_in_use_cannot_be_deleted()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var tract = await NewTract(pm, project.Id);
        var link = await Ok<PartyLinkDto>(await AddToTract(pm, project.Id, tract.Id, NewPersonLink("Pat Owner", "FeeOwner", 50m)), HttpStatusCode.Created);
        var duplicate = await AddToTract(pm, project.Id, tract.Id, Link(link.Party.Id, "FeeOwner", 10m));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadAsStringAsync()).Should().Contain("party_link_exists");

        var edited = await Ok<PartyLinkDto>(await pm.Http.PutAsJsonAsync($"/api/v1/party-links/{link.Id}",
            new { sharePercent = 75m, notes = "Corrected from the deed", leaseOnFile = false }, Json));
        edited.SharePercent.Should().Be(75m);
        edited.Notes.Should().Be("Corrected from the deed");

        var blocked = await pm.Http.DeleteAsync($"/api/v1/parties/{link.Party.Id}");
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("party_in_use");

        (await pm.Http.DeleteAsync($"/api/v1/party-links/{link.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await pm.Http.DeleteAsync($"/api/v1/party-links/{link.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await pm.Http.DeleteAsync($"/api/v1/parties/{link.Party.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await pm.Http.GetAsync($"/api/v1/parties/{link.Party.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PgFact]
    public async Task AT_PM_22_ROW_014_The_party_list_searches_filters_and_pages()
    {
        var pm = await _api.As("ProjectManager");
        var tag = Guid.NewGuid().ToString("N")[..8];
        await pm.Http.PostAsJsonAsync("/api/v1/parties", PartyBody($"Zed {tag} Person"), Json);
        await pm.Http.PostAsJsonAsync("/api/v1/parties", PartyBody($"Acme {tag} LLC", "Organization"), Json);
        await pm.Http.PostAsJsonAsync("/api/v1/parties", PartyBody($"Mid {tag} Person"), Json);

        var all = await Ok<PagedResult<PartySummaryDto>>(await pm.Http.GetAsync($"/api/v1/parties?search={tag}"));
        all.Items.Select(p => p.Name).Should().Equal($"Acme {tag} LLC", $"Mid {tag} Person", $"Zed {tag} Person");
        (await Ok<PagedResult<PartySummaryDto>>(await pm.Http.GetAsync($"/api/v1/parties?search={tag}&kind=Organization"))).Items.Should().ContainSingle();
        var page = await Ok<PagedResult<PartySummaryDto>>(await pm.Http.GetAsync($"/api/v1/parties?search={tag}&page=2&pageSize=2"));
        page.Total.Should().Be(3);
        page.Items.Should().ContainSingle().Which.Name.Should().Be($"Zed {tag} Person");
        (await pm.Http.GetAsync("/api/v1/parties?pageSize=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync("/api/v1/parties?page=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync("/api/v1/parties?kind=Alien")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [PgFact]
    public async Task AT_PM_22_ROW_007_014_Splitting_a_parcel_keeps_the_people_on_every_piece()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var parcel = await NewParcel(pm, project.Id, null, 10_000m);
        await Ok<PartyLinkDto>(await AddToParcel(pm, parcel.Id, NewPersonLink("Pat Owner", "FeeOwner", 100m)), HttpStatusCode.Created);
        await Ok<PartyLinkDto>(await AddToParcel(pm, parcel.Id, NewPersonLink("Lee Lawyer", "Attorney")), HttpStatusCode.Created);

        var split = await Ok<ParcelSplitResultDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/split",
            new { parts = new[] { new { areaSqFt = 3_000m } } }, Json), HttpStatusCode.Created);

        foreach (var piece in new[] { split.Parts.Single().Id, split.Remnant!.Id })
        {
            var seen = await Ok<ParcelPartiesDto>(await pm.Http.GetAsync($"/api/v1/parcels/{piece}/parties"));
            seen.Ownership.Should().ContainSingle().Which.Party.Name.Should().Be("Pat Owner");
            seen.Contacts.Should().ContainSingle().Which.Party.Name.Should().Be("Lee Lawyer");
        }
        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(distinct party_id) from party_links where parcel_id = any(@ids)", ("ids", new[] { parcel.Id, split.Parts.Single().Id, split.Remnant.Id }))).Should().Be(2,
            "the people are shared across the pieces, not copied");
    }

    // ── tax information (ROW-018) ──

    [PgFact]
    public async Task AT_PM_22_ROW_018_A_tax_number_is_encrypted_masked_everywhere_and_never_in_the_audit_trail()
    {
        var pm = await _api.As("ProjectManager");
        var party = await NewParty(pm);
        const string number = "123-45-6789";

        var set = await pm.Http.PutAsJsonAsync($"/api/v1/parties/{party.Id}/tax-info", new { type = "Ssn", taxId = number, w9ReceivedOn = "2026-01-15", reportable1099 = true }, Json);
        var setBody = await set.Content.ReadAsStringAsync();
        set.StatusCode.Should().Be(HttpStatusCode.OK, setBody);
        setBody.Should().NotContain("123456789").And.NotContain(number);
        var dto = JsonSerializer.Deserialize<PartyDto>(setBody, Json)!;
        dto.TaxIdMasked.Should().Be("***-**-6789");
        dto.W9ReceivedOn.Should().Be(new DateOnly(2026, 1, 15));
        dto.Reportable1099.Should().BeTrue();

        foreach (var url in new[] { $"/api/v1/parties/{party.Id}", $"/api/v1/parties?search={Uri.EscapeDataString(party.Name)}" })
        {
            var body = await (await pm.Http.GetAsync(url)).Content.ReadAsStringAsync();
            body.Should().NotContain("123456789").And.NotContain(number).And.NotContain("v1.");
        }

        var stored = (string)(await PgTestDatabase.QueryAsync(_api.Database, "select tax_id_protected from parties where id = @id", ("id", party.Id)))[0][0]!;
        stored.Should().StartWith("v1.").And.NotContain("123456789").And.NotContain("6789");

        (await pm.Http.PutAsJsonAsync($"/api/v1/parties/{party.Id}/tax-info", new { type = "Ssn", taxId = "234-56-7891", reportable1099 = true }, Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        await pm.Http.DeleteAsync($"/api/v1/parties/{party.Id}/tax-info/number");

        var audit = await PgTestDatabase.QueryAsync(_api.Database,
            "select coalesce(field,''), coalesce(old_value,''), coalesce(new_value,'') from audit_log where entity_type = 'Party' and entity_id = @id", ("id", party.Id));
        audit.Should().NotBeEmpty();
        var text = string.Join("\n", audit.Select(r => $"{r[0]}|{r[1]}|{r[2]}"));
        text.Should().NotContain("123456789").And.NotContain("234567891").And.NotContain("6789").And.NotContain("7891").And.NotContain("v1.");
        audit.Should().Contain(r => (string)r[0]! == "TaxIdProtected", "that the number changed is still recorded");
    }

    [PgFact]
    public async Task AT_PM_22_ROW_018_A_bad_tax_number_is_a_400_that_never_repeats_the_number()
    {
        var pm = await _api.As("ProjectManager");
        var party = await NewParty(pm);

        foreach (var (type, number) in new[] { ("Ssn", "666-12-3456"), ("Ssn", "123-45-678"), ("Ein", "00-1234567"), ("Itin", "123-45-6789"), ("Ssn", "12345678a") })
        {
            var res = await pm.Http.PutAsJsonAsync($"/api/v1/parties/{party.Id}/tax-info", new { type, taxId = number }, Json);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await res.Content.ReadAsStringAsync();
            body.Should().NotContain(number);
        }
        (await Status(pm.Http.PutAsJsonAsync($"/api/v1/parties/{party.Id}/tax-info", new { taxId = "123-45-6789" }, Json))).Should().Be(HttpStatusCode.BadRequest, "a number needs a type");
        (await Status(pm.Http.PutAsJsonAsync($"/api/v1/parties/{party.Id}/tax-info", new { w9ReceivedOn = "2999-01-01" }, Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Ok<PartyDto>(await pm.Http.GetAsync($"/api/v1/parties/{party.Id}"))).HasTaxId.Should().BeFalse();
    }

    [PgFact]
    public async Task AT_PM_22_ROW_018_Keeping_the_number_while_updating_the_W9_date_and_clearing_it_work_as_described()
    {
        var pm = await _api.As("ProjectManager");
        var party = await NewParty(pm);
        await pm.Http.PutAsJsonAsync($"/api/v1/parties/{party.Id}/tax-info", new { type = "Ein", taxId = "12-3456789", reportable1099 = true }, Json);

        var kept = await Ok<PartyDto>(await pm.Http.PutAsJsonAsync($"/api/v1/parties/{party.Id}/tax-info", new { type = "Ein", w9ReceivedOn = "2026-02-01", reportable1099 = true }, Json));
        kept.TaxIdMasked.Should().Be("**-***6789");
        kept.W9ReceivedOn.Should().Be(new DateOnly(2026, 2, 1));

        (await Status(pm.Http.PutAsJsonAsync($"/api/v1/parties/{party.Id}/tax-info", new { type = "Ssn" }, Json))).Should().Be(HttpStatusCode.BadRequest, "a new type needs the number again");

        var cleared = await Ok<PartyDto>(await pm.Http.DeleteAsync($"/api/v1/parties/{party.Id}/tax-info/number"));
        cleared.HasTaxId.Should().BeFalse();
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parties where id = @id and tax_id_protected is null and tax_id_last4 is null", ("id", party.Id))).Should().Be(1);
    }

    // ── every role, both directions (AT-*-parties) ──

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task Each_role_can_do_exactly_what_the_parties_page_allows_on_parties_links_and_tax_information(string role)
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var tract = await NewTract(pm, project.Id);
        var parcel = await NewParcel(pm, project.Id, tract.Id);
        var seeded = await Ok<PartyLinkDto>(await AddToTract(pm, project.Id, tract.Id, NewPersonLink("Seed Owner", "FeeOwner", 10m)), HttpStatusCode.Created);
        var user = await _api.As(role);
        var read = Spec.Can(role, "parties", "read");
        var write = Spec.Can(role, "parties", "create");
        var update = Spec.Can(role, "parties", "update");
        var delete = Spec.Can(role, "parties", "delete");

        (await Status(user.Http.GetAsync("/api/v1/parties"))).Should().Be(read ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        (await Status(user.Http.GetAsync($"/api/v1/parties/{seeded.Party.Id}"))).Should().Be(read ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        (await Status(user.Http.GetAsync($"/api/v1/parties/{seeded.Party.Id}/usage"))).Should().Be(read ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        (await Status(user.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/parties"))).Should().Be(read ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        (await Status(user.Http.GetAsync($"/api/v1/projects/{project.Id}/tracts/{tract.Id}/parties"))).Should().Be(read ? HttpStatusCode.OK : HttpStatusCode.Forbidden);

        var created = await user.Http.PostAsJsonAsync("/api/v1/parties", PartyBody($"By {role}"), Json);
        created.StatusCode.Should().Be(write ? HttpStatusCode.Created : HttpStatusCode.Forbidden);
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/parties/{seeded.Party.Id}", PartyBody("Seed Owner"), Json))).Should().Be(update ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/parties/{seeded.Party.Id}/tax-info", new { type = "Ssn", taxId = "123-45-6789", reportable1099 = false }, Json)))
            .Should().Be(update ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        (await Status(AddToParcel(user, parcel.Id, NewPersonLink($"Contact {role}", "Attorney")))).Should().Be(write ? HttpStatusCode.Created : HttpStatusCode.Forbidden);
        (await Status(AddToTract(user, project.Id, tract.Id, NewPersonLink($"Holder {role}", "Lienholder")))).Should().Be(write ? HttpStatusCode.Created : HttpStatusCode.Forbidden);
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/party-links/{seeded.Id}", new { sharePercent = 20m, leaseOnFile = false }, Json))).Should().Be(update ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        (await Status(user.Http.DeleteAsync($"/api/v1/parties/{seeded.Party.Id}/tax-info/number"))).Should().Be(update ? HttpStatusCode.OK : HttpStatusCode.Forbidden);

        var throwaway = await NewParty(pm);
        (await Status(user.Http.DeleteAsync($"/api/v1/parties/{throwaway.Id}"))).Should().Be(delete ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden);
        (await Status(user.Http.DeleteAsync($"/api/v1/party-links/{seeded.Id}"))).Should().Be(delete ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden);

        if (!write)
            (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parties where name = @n", ("n", $"By {role}"))).Should().Be(0, "a refused request must change nothing");
    }

    public static IEnumerable<object[]> Roles() => AllRoles.Select(r => new object[] { r });

    [PgFact]
    public async Task Without_signing_in_nothing_about_parties_is_available()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        foreach (var url in new[] { "/api/v1/parties", $"/api/v1/parties/{id}", $"/api/v1/parties/{id}/usage", $"/api/v1/parcels/{id}/parties" })
            (await anon.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
        (await anon.PutAsJsonAsync($"/api/v1/parties/{id}/tax-info", new { type = "Ssn", taxId = "123-45-6789" }, Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.DeleteAsync($"/api/v1/party-links/{id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── races ──

    [PgFact]
    public async Task Ten_people_adding_the_same_party_in_the_same_role_at_once_produce_exactly_one_link()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var tract = await NewTract(pm, project.Id);
        var party = await NewParty(pm);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => AddToTract(pm, project.Id, tract.Id, Link(party.Id, "Attorney"))));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Where(r => r.StatusCode != HttpStatusCode.Created).Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Conflict);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from party_links where parent_tract_id = @t and party_id = @p", ("t", tract.Id), ("p", party.Id))).Should().Be(1);
    }

    [PgFact]
    public async Task Owners_added_at_the_same_moment_can_never_claim_more_than_the_whole_tract()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var tract = await NewTract(pm, project.Id);

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => AddToTract(pm, project.Id, tract.Id, NewPersonLink($"Racer {i}", "FeeOwner", 20m))));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.Conflict, "never a 500");
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().BeInRange(1, 5);
        var total = Convert.ToDecimal((await PgTestDatabase.QueryAsync(_api.Database,
            "select coalesce(sum(share_percent), 0) from party_links where parent_tract_id = @t and role = 'FeeOwner'", ("t", tract.Id)))[0][0]);
        total.Should().BeLessThanOrEqualTo(100m, "the database must never hold shares above the whole");

        // After the dust settles, filling the rest one at a time reaches exactly the whole.
        var remaining = 100m - total;
        if (remaining > 0)
            await Ok<PartyLinkDto>(await AddToTract(pm, project.Id, tract.Id, NewPersonLink("Finisher", "FeeOwner", remaining)), HttpStatusCode.Created);
        (await Status(AddToTract(pm, project.Id, tract.Id, NewPersonLink("One too many", "FeeOwner", 0.0001m)))).Should().Be(HttpStatusCode.Conflict);
    }

    // ── tenancy ──

    [PgFact]
    public async Task One_agency_never_sees_or_changes_another_agencys_parties_or_links()
    {
        await EnsureOtherTenantAsync();
        var mine = await _api.As("ProjectManager");
        var theirs = await _api.As("ProjectManager", OtherTenant);
        var project = await NewProject(mine);
        var tract = await NewTract(mine, project.Id);
        var parcel = await NewParcel(mine, project.Id, tract.Id);
        var link = await Ok<PartyLinkDto>(await AddToTract(mine, project.Id, tract.Id, NewPersonLink("Secret Owner", "FeeOwner", 100m)), HttpStatusCode.Created);
        await mine.Http.PutAsJsonAsync($"/api/v1/parties/{link.Party.Id}/tax-info", new { type = "Ssn", taxId = "123-45-6789" }, Json);

        (await Read<PagedResult<PartySummaryDto>>(await theirs.Http.GetAsync("/api/v1/parties?search=Secret"))).Items.Should().BeEmpty();
        foreach (var url in new[] { $"/api/v1/parties/{link.Party.Id}", $"/api/v1/parties/{link.Party.Id}/usage", $"/api/v1/parcels/{parcel.Id}/parties", $"/api/v1/projects/{project.Id}/tracts/{tract.Id}/parties" })
            (await theirs.Http.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound, url);
        (await theirs.Http.PutAsJsonAsync($"/api/v1/parties/{link.Party.Id}", PartyBody("Hijacked"), Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PutAsJsonAsync($"/api/v1/parties/{link.Party.Id}/tax-info", new { type = "Ssn", taxId = "234-56-7891" }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.DeleteAsync($"/api/v1/parties/{link.Party.Id}/tax-info/number")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.DeleteAsync($"/api/v1/parties/{link.Party.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PutAsJsonAsync($"/api/v1/party-links/{link.Id}", new { sharePercent = 1m, leaseOnFile = false }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.DeleteAsync($"/api/v1/party-links/{link.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AddToTract(theirs, project.Id, tract.Id, NewPersonLink("Intruder"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AddToParcel(theirs, parcel.Id, NewPersonLink("Intruder", "Attorney"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Their own agency cannot link my party either.
        var theirProject = await NewProject(theirs);
        var theirTract = await NewTract(theirs, theirProject.Id);
        (await AddToTract(theirs, theirProject.Id, theirTract.Id, Link(link.Party.Id, "FeeOwner", 10m))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var mineAgain = await Ok<PartyDto>(await mine.Http.GetAsync($"/api/v1/parties/{link.Party.Id}"));
        mineAgain.Name.Should().Be("Secret Owner");
        mineAgain.TaxIdMasked.Should().Be("***-**-6789");
    }

    // ── schema ──

    [PgFact]
    public async Task The_database_itself_refuses_a_link_with_no_target_or_two_and_a_second_copy_of_a_link()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var tract = await NewTract(pm, project.Id);
        var parcel = await NewParcel(pm, project.Id);
        var party = await NewParty(pm);

        string Insert(string tractId, string parcelId, string role = "Attorney") =>
            $"insert into party_links (id, tenant_id, party_id, parent_tract_id, parcel_id, role, lease_on_file, created_at, created_by, updated_at, updated_by) " +
            $"values (gen_random_uuid(), @tenant, @party, {tractId}, {parcelId}, '{role}', false, now(), @tenant, now(), @tenant)";
        var args = new (string, object)[] { ("tenant", pm.TenantId), ("party", party.Id), ("tract", tract.Id), ("parcel", parcel.Id) };

        var neither = () => PgTestDatabase.QueryAsync(_api.Database, Insert("null", "null"), args);
        (await neither.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_party_links_one_target");
        var both = () => PgTestDatabase.QueryAsync(_api.Database, Insert("@tract", "@parcel"), args);
        (await both.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ck_party_links_one_target");

        await PgTestDatabase.QueryAsync(_api.Database, Insert("@tract", "null"), args);
        var twice = () => PgTestDatabase.QueryAsync(_api.Database, Insert("@tract", "null"), args);
        (await twice.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23505");
        await PgTestDatabase.QueryAsync(_api.Database, Insert("@tract", "null", "Bank"), args);
    }

    [PgFact]
    public async Task The_party_tables_have_the_keys_indexes_and_constraints_the_code_relies_on()
    {
        var indexes = (await PgTestDatabase.QueryAsync(_api.Database, "select indexname from pg_indexes where tablename in ('party_links', 'parties')")).Select(r => (string)r[0]!).ToList();
        indexes.Should().Contain(new[] { "ix_party_links_parcel_id_party_id_role", "ix_party_links_parent_tract_id_party_id_role", "ix_party_links_party_id", "ix_parties_tenant_id_name" });
        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from pg_constraint where conrelid = 'party_links'::regclass and contype = 'f'")).Should().Be(3, "party, tract and parcel");
        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from information_schema.columns where table_name = 'parties' and column_name in ('tax_id_protected', 'tax_id_last4', 'tax_id_type', 'w9received_on', 'w9_received_on', 'reportable1099', 'reportable1099')")).Should().BeGreaterThanOrEqualTo(4);
        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from information_schema.columns where table_name = 'parties' and column_name in ('tax_id', 'ssn', 'tin')")).Should().Be(0, "no plain-text column may exist");
    }

    // ── hostile and awkward input ──

    [PgTheory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"kind\":\"Alien\",\"name\":\"x\"}")]
    [InlineData("{\"kind\":7,\"name\":\"x\"}")]
    [InlineData("{\"name\":123}")]
    [InlineData("{\"name\":\"\"}")]
    public async Task A_malformed_party_body_is_a_400_never_a_500(string body)
    {
        var pm = await _api.As("ProjectManager");

        var res = await pm.PostJson("/api/v1/parties", body);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest, await res.Content.ReadAsStringAsync());
    }

    [PgFact]
    public async Task Hostile_text_in_party_fields_is_stored_literally_or_refused_never_run()
    {
        var pm = await _api.As("ProjectManager");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var scriptName = $"<script>alert('{tag}')</script>";
        var sqlName = $"Robert'); DROP TABLE parties;-- {tag}";

        var a = await Ok<PartyDto>(await pm.Http.PostAsJsonAsync("/api/v1/parties", PartyBody(scriptName), Json), HttpStatusCode.Created);
        var b = await Ok<PartyDto>(await pm.Http.PostAsJsonAsync("/api/v1/parties", PartyBody(sqlName), Json), HttpStatusCode.Created);

        a.Name.Should().Be(scriptName, "stored as typed; the screens escape it");
        b.Name.Should().Be(sqlName);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parties where id = any(@ids)", ("ids", new[] { a.Id, b.Id }))).Should().Be(2);
        var found = await Ok<PagedResult<PartySummaryDto>>(await pm.Http.GetAsync($"/api/v1/parties?search={Uri.EscapeDataString("'); drop table parties;--")}"));
        found.Items.Should().Contain(p => p.Id == b.Id, "the text is searched for, not run");
        (await pm.PostJson("/api/v1/parties", JsonSerializer.Serialize(PartyBody("null\0char")))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.PostJson("/api/v1/parties", JsonSerializer.Serialize(PartyBody(new string('x', 201))))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.PostJson("/api/v1/parties", JsonSerializer.Serialize(new { kind = "Person", name = "Bad email", email = "nope" }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.PostJson("/api/v1/parties", JsonSerializer.Serialize(new { kind = "Person", name = "Bad state", state = "Illinois" }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [PgFact]
    public async Task Bad_links_are_400s_and_unknown_ids_are_404s()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var tract = await NewTract(pm, project.Id);
        var party = await NewParty(pm);

        (await Status(AddToTract(pm, project.Id, tract.Id, new { role = "FeeOwner" }))).Should().Be(HttpStatusCode.BadRequest, "neither an existing party nor a new one");
        (await Status(AddToTract(pm, project.Id, tract.Id, new { partyId = party.Id, newParty = PartyBody(), role = "FeeOwner" }))).Should().Be(HttpStatusCode.BadRequest, "both");
        (await Status(AddToTract(pm, project.Id, tract.Id, new { partyId = party.Id, role = "Wizard" }))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(AddToTract(pm, project.Id, tract.Id, Link(party.Id, "FeeOwner", 0m)))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(AddToTract(pm, project.Id, tract.Id, Link(party.Id, "FeeOwner", 101m)))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(AddToTract(pm, project.Id, tract.Id, Link(party.Id, "FeeOwner", 33.33333m)))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(AddToTract(pm, project.Id, tract.Id, Link(party.Id, "Lienholder", 10m)))).Should().Be(HttpStatusCode.BadRequest, "only some roles have a share");
        (await Status(AddToTract(pm, project.Id, tract.Id, new { partyId = party.Id, role = "Attorney", leaseOnFile = true }))).Should().Be(HttpStatusCode.BadRequest, "only tenants have a lease");
        (await Status(AddToTract(pm, project.Id, tract.Id, new { partyId = party.Id, role = "Attorney", notes = new string('n', 501) }))).Should().Be(HttpStatusCode.BadRequest);
        (await Status(AddToTract(pm, project.Id, tract.Id, Link(Guid.NewGuid(), "Attorney")))).Should().Be(HttpStatusCode.NotFound);
        (await Status(AddToTract(pm, project.Id, Guid.NewGuid(), Link(party.Id, "Attorney")))).Should().Be(HttpStatusCode.NotFound);
        (await Status(AddToParcel(pm, Guid.NewGuid(), Link(party.Id, "Attorney")))).Should().Be(HttpStatusCode.NotFound);
        (await Status(pm.Http.GetAsync("/api/v1/parties/not-a-guid"))).Should().Be(HttpStatusCode.NotFound);
        (await Status(pm.Http.PutAsJsonAsync($"/api/v1/party-links/{Guid.NewGuid()}", new { leaseOnFile = false }, Json))).Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from party_links where parent_tract_id = @t", ("t", tract.Id))).Should().Be(0, "nothing from the bad requests was saved");
    }

    [PgFact]
    public async Task A_superseded_parcel_can_no_longer_take_new_people()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var parcel = await NewParcel(pm, project.Id, null, 1_000m);
        await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/split", new { parts = new[] { new { areaSqFt = 400m } } }, Json);

        var res = await AddToParcel(pm, parcel.Id, NewPersonLink("Late", "Attorney"));

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await res.Content.ReadAsStringAsync()).Should().Contain("parcel_not_active");
    }

    // ── audit ──

    [PgFact]
    public async Task Every_party_and_link_change_is_in_the_audit_trail_with_who_did_it()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var tract = await NewTract(pm, project.Id);
        var link = await Ok<PartyLinkDto>(await AddToTract(pm, project.Id, tract.Id, NewPersonLink("Audited Owner", "FeeOwner", 50m)), HttpStatusCode.Created);
        await pm.Http.PutAsJsonAsync($"/api/v1/party-links/{link.Id}", new { sharePercent = 55m, leaseOnFile = false }, Json);
        await pm.Http.PutAsJsonAsync($"/api/v1/parties/{link.Party.Id}", PartyBody("Audited Owner Renamed"), Json);

        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_type = 'Party' and entity_id = @id and changed_by = @u", ("id", link.Party.Id), ("u", pm.UserId))).Should().BeGreaterThanOrEqualTo(2);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_type = 'Party' and entity_id = @id and field = 'Name' and new_value = 'Audited Owner Renamed'", ("id", link.Party.Id))).Should().Be(1);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_type = 'PartyLink' and entity_id = @id and field = 'SharePercent'", ("id", link.Id))).Should().Be(1);
    }

    // ── the seeded demo and configuration ──

    [PgFact]
    public async Task The_demo_agency_opens_with_owners_a_lienholder_a_tenant_and_an_abutter_but_no_tax_numbers()
    {
        var pm = await _api.As("ProjectManager");
        var bridge = (await Ok<PagedResult<ProjectSummaryDto>>(await pm.Http.GetAsync("/api/v1/projects?search=DEMO-002"))).Items.Single();
        var parcels = await Ok<PagedResult<ParcelSummaryDto>>(await pm.Http.GetAsync($"/api/v1/parcels?projectId={bridge.Id}"));

        var atOffer = await Ok<ParcelPartiesDto>(await pm.Http.GetAsync($"/api/v1/parcels/{parcels.Items.Single(p => p.Number == "001").Id}/parties"));
        atOffer.Ownership.Where(l => l.Role == PartyRole.FeeOwner).Select(l => (l.Party.Name, l.SharePercent)).Should().BeEquivalentTo(new[] { ("Harbor Holdings LLC", (decimal?)60m), ("Alex Marlowe", (decimal?)40m) });
        atOffer.Ownership.Should().Contain(l => l.Role == PartyRole.Lienholder && l.Party.Name == "First Harbor Bank");
        atOffer.Contacts.Should().ContainSingle().Which.Role.Should().Be(PartyRole.Attorney);
        atOffer.Occupants.Should().ContainSingle().Which.LeaseOnFile.Should().BeTrue();
        var pier = await Ok<ParcelPartiesDto>(await pm.Http.GetAsync($"/api/v1/parcels/{parcels.Items.Single(p => p.Number == "003").Id}/parties"));
        pier.Abutters.Should().ContainSingle();
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parties where tax_id_protected is not null and name in ('Harbor Holdings LLC', 'Alex Marlowe')")).Should().Be(0);
    }

    [Fact]
    public async Task Production_refuses_to_start_without_a_tax_id_key()
    {
        await using var factory = new RowApiFactory("Production", new() { ["Jwt:SigningKey"] = new string('p', 48), ["ConnectionStrings:Postgres"] = "Host=unused" });

        var act = () => factory.CreateClient().GetAsync("/health");

        (await act.Should().ThrowAsync<Exception>()).And.ToString().Should().Contain("Security:TaxIdKey");
    }

    [Fact]
    public async Task The_tax_id_key_must_be_the_right_size_when_given()
    {
        await using var factory = new RowApiFactory("Production", new()
        {
            ["Jwt:SigningKey"] = new string('p', 48), ["ConnectionStrings:Postgres"] = "Host=unused", ["Security:TaxIdKey"] = Convert.ToBase64String(new byte[10]),
        });

        var act = () => factory.CreateClient().GetAsync("/health");

        (await act.Should().ThrowAsync<Exception>()).And.ToString().Should().Contain("32 bytes");
    }
}
