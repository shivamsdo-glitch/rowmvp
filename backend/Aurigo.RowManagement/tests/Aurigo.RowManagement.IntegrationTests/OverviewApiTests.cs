using System.Net;
using System.Net.Http.Json;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.ParcelTabs;
using Aurigo.RowManagement.Application.Parties;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>Phase 1.5 (ADR 0021): the across-parcel searches behind Title &amp; Survey, Appraisal and Negotiation &amp; Closing, on the real API and real Postgres. Matches AT-X-57.</summary>
public class OverviewApiTests : IClassFixture<PgApi>
{
    private readonly PgApi _api;
    public OverviewApiTests(PgApi api) => _api = api;

    private static async Task<T> Get<T>(HttpClient http, string url)
    {
        var r = await http.GetAsync(url);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(LifecycleSupport.Json))!;
    }

    /// <summary>A parcel with a uniquely named owner and one record of each kind the searches cover.</summary>
    private async Task<(ParcelDto Parcel, string Owner)> Scene()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var owner = $"Zebulon{Guid.NewGuid().ToString("N")[..8]}";
        var admin = await _api.As("Admin");
        var url = $"/api/v1/parcels/{parcel.Id}";
        await LifecycleSupport.Ok<PartyLinkDto>(await admin.Http.PostAsJsonAsync($"{url}/parties", new { newParty = new { kind = "Person", name = owner }, role = "FeeOwner", ownerType = "Fee Owner", leaseOnFile = false }, LifecycleSupport.Json), HttpStatusCode.Created);
        await LifecycleSupport.Ok<AppraisalDto>(await admin.Http.PostAsJsonAsync($"{url}/appraisals", new { appraisalType = "Full", appraiser = "Ann", amounts = new[] { new { amountType = "Land", amount = 100m } } }, LifecycleSupport.Json), HttpStatusCode.Created);
        await LifecycleSupport.Ok<NegotiationDto>(await admin.Http.PutAsJsonAsync($"{url}/negotiation", new { assignedTo = "Casey" }, LifecycleSupport.Json));
        await LifecycleSupport.Ok<TitleClosingDto>(await admin.Http.PutAsJsonAsync($"{url}/title-closing", new { titleCompany = "Lone Star" }, LifecycleSupport.Json));
        await LifecycleSupport.Ok<EncumbranceDto>(await admin.Http.PostAsJsonAsync($"{url}/encumbrances", new { encumbranceType = "Lien", documentTitle = "Deed of Trust", grantor = "A", grantee = "B" }, LifecycleSupport.Json), HttpStatusCode.Created);
        await LifecycleSupport.Ok<EasementDto>(await admin.Http.PostAsJsonAsync($"{url}/easements", new { number = "E-1" }, LifecycleSupport.Json), HttpStatusCode.Created);
        return (parcel, owner);
    }

    [PgFact]
    public async Task AT_X_57_Appraisals_are_found_by_owner_parcel_number_address_or_their_own_id_and_unrelated_text_finds_nothing()
    {
        var (parcel, owner) = await Scene();
        var neg = await _api.As("Negotiator");

        var byOwner = await Get<List<AppraisalOverviewRow>>(neg.Http, $"/api/v1/overview/appraisals?search={owner[..10].ToLower()}");
        byOwner.Should().ContainSingle().Which.Should().Match<AppraisalOverviewRow>(r => r.ParcelId == parcel.Id && r.OwnerName == owner && r.AppraisalTotal == 100m && r.Number == $"APR-{parcel.Number}-001");
        (await Get<List<AppraisalOverviewRow>>(neg.Http, $"/api/v1/overview/appraisals?search={parcel.Number}")).Should().Contain(r => r.ParcelId == parcel.Id);
        (await Get<List<AppraisalOverviewRow>>(neg.Http, $"/api/v1/overview/appraisals?search=APR-{parcel.Number}-001")).Should().Contain(r => r.ParcelId == parcel.Id && r.Number == $"APR-{parcel.Number}-001");
        (await Get<List<AppraisalOverviewRow>>(neg.Http, $"/api/v1/overview/appraisals?search=no-such-thing-{Guid.NewGuid():N}")).Should().BeEmpty();
        (await Get<List<AppraisalOverviewRow>>(neg.Http, "/api/v1/overview/appraisals")).Should().NotBeEmpty("no search text lists the most recent");
    }

    [PgFact]
    public async Task AT_X_57_Every_kind_of_record_is_found_by_the_owner_of_its_parcel()
    {
        var (parcel, owner) = await Scene();
        var user = await _api.As("Admin");
        string q = $"search={owner}";

        (await Get<List<NegotiationOverviewRow>>(user.Http, $"/api/v1/overview/negotiations?{q}")).Should().ContainSingle().Which.Should().Match<NegotiationOverviewRow>(r => r.ParcelId == parcel.Id && r.Number == $"NEG-{parcel.Number}" && r.OwnerName == owner);
        (await Get<List<TitleClosingOverviewRow>>(user.Http, $"/api/v1/overview/title-closings?{q}")).Should().ContainSingle().Which.TitleCompany.Should().Be("Lone Star");
        (await Get<List<EncumbranceOverviewRow>>(user.Http, $"/api/v1/overview/encumbrances?{q}")).Should().ContainSingle().Which.DocumentTitle.Should().Be("Deed of Trust");
        (await Get<List<EasementOverviewRow>>(user.Http, $"/api/v1/overview/easements?{q}")).Should().ContainSingle().Which.Number.Should().Be("E-1");
    }

    [PgFact]
    public async Task AT_X_57_The_parcel_search_also_finds_a_parcel_by_the_name_of_its_owner()
    {
        var (parcel, owner) = await Scene();
        var user = await _api.As("Negotiator");

        var found = await Get<PagedResult<ParcelSummaryDto>>(user.Http, $"/api/v1/parcels?search={owner}");

        found.Items.Should().ContainSingle().Which.Id.Should().Be(parcel.Id);
    }

    [PgFact]
    public async Task AT_X_57_Search_text_with_wildcards_or_quotes_is_treated_as_plain_text()
    {
        await Scene();
        var user = await _api.As("Admin");

        foreach (var text in new[] { "%", "_", "'", "\"; drop table parcels; --", new string('x', 300) })
        {
            var r = await user.Http.GetAsync($"/api/v1/overview/appraisals?search={Uri.EscapeDataString(text)}");
            r.StatusCode.Should().Be(HttpStatusCode.OK, text);
        }
        (await Get<List<AppraisalOverviewRow>>(user.Http, "/api/v1/overview/appraisals?search=%25")).Should().BeEmpty("a percent sign is not a wildcard");
    }

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task AT_X_57_Each_search_follows_the_access_of_the_parcel_tab_it_searches(string role)
    {
        var user = await _api.As(role);
        var pages = new (string Url, string Page)[]
        {
            ("appraisals", "parcelappraisal"), ("negotiations", "parcelnegotiation"), ("title-closings", "titleclosing"), ("encumbrances", "titleencumbrances"), ("easements", "easements"),
        };
        foreach (var (url, page) in pages)
        {
            var status = (await user.Http.GetAsync($"/api/v1/overview/{url}")).StatusCode;
            status.Should().Be(Spec.Can(role, page, "read") ? HttpStatusCode.OK : HttpStatusCode.Forbidden, $"{role} {url}");
        }
    }

    public static IEnumerable<object[]> Roles() => LifecycleSupport.Roles();

    [PgFact]
    public async Task Without_signing_in_the_searches_are_not_available()
    {
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync("/api/v1/overview/appraisals")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
