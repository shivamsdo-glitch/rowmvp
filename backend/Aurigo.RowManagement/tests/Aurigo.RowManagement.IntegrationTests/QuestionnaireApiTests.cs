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

/// <summary>Phase 1.5 slice 7b (ADR 0019): a parcel's relocation questionnaires on the real API and real Postgres. Matches AT-RA-40, AT-X-53.</summary>
public class QuestionnaireApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly PgApi _api;
    public QuestionnaireApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> t) => (await t).StatusCode;

    private async Task<ParcelDto> NewParcel()
    {
        var admin = await _api.As("Admin");
        var project = await Ok<ProjectDto>(await admin.Http.PostAsJsonAsync("/api/v1/projects", new { name = $"QS {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local" }, Json), HttpStatusCode.Created);
        return await Ok<ParcelDto>(await admin.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 100m, acquisitionType = "Standard" }, Json), HttpStatusCode.Created);
    }

    private static Dictionary<string, object?> Body(Dictionary<string, object?>? extra = null)
    {
        var b = new Dictionary<string, object?>
        {
            ["relocationType"] = "Residential", ["dueByDate"] = "2026-08-01", ["sentToOwnerDate"] = "2026-07-15",
            ["interests"] = new[] { new { name = "Kamali", phone = "555-0100", contact = "Alex", email = "k@example.com", address = "1 Oak St" } },
            ["questions"] = new[] { new { question = "Do you rent?", answer = (string?)"No", notes = (string?)"Owner occupied." }, new { question = "Household size?", answer = (string?)"4", notes = (string?)null } },
        };
        if (extra is not null) foreach (var kv in extra) b[kv.Key] = kv.Value;
        return b;
    }

    [PgFact]
    public async Task AT_RA_40_A_relocation_agent_adds_changes_and_removes_questionnaires_numbered_per_parcel()
    {
        var parcel = await NewParcel();
        var ra = await _api.As("RelocationAgent");
        var url = $"/api/v1/parcels/{parcel.Id}/questionnaires";

        var first = await Ok<QuestionnaireDto>(await ra.Http.PostAsJsonAsync(url, Body(), Json), HttpStatusCode.Created);
        var second = await Ok<QuestionnaireDto>(await ra.Http.PostAsJsonAsync(url, Body(new() { ["relocationType"] = "Non-residential" }), Json), HttpStatusCode.Created);
        first.Number.Should().Be($"QST-{parcel.Number}-001");
        second.Number.Should().Be($"QST-{parcel.Number}-002");
        first.Interests.Should().ContainSingle().Which.Name.Should().Be("Kamali");
        first.Questions.Select(q => q.Question).Should().Equal("Do you rent?", "Household size?");
        first.SentToOwnerDate.Should().Be(new DateOnly(2026, 7, 15));

        var changed = await Ok<QuestionnaireDto>(await ra.Http.PutAsJsonAsync($"{url}/{first.Id}", Body(new() { ["relocationType"] = "Personal Property only", ["questions"] = Array.Empty<object>() }), Json));
        changed.RelocationType.Should().Be("Personal Property only");
        changed.Questions.Should().BeEmpty("a save replaces the grids as sent");
        changed.Interests.Should().HaveCount(1);
        changed.Number.Should().Be(first.Number);

        (await Ok<List<QuestionnaireDto>>(await ra.Http.GetAsync(url))).Select(q => q.Number).Should().Equal(first.Number, second.Number);
        (await Status(ra.Http.DeleteAsync($"{url}/{second.Id}"))).Should().Be(HttpStatusCode.NoContent);
        (await Status(ra.Http.DeleteAsync($"{url}/{second.Id}"))).Should().Be(HttpStatusCode.NotFound);
        (await Ok<List<QuestionnaireDto>>(await ra.Http.GetAsync(url))).Should().ContainSingle();
    }

    [PgFact]
    public async Task AT_RA_40_Ten_adds_at_once_get_ten_different_numbers()
    {
        var parcel = await NewParcel();
        var ra = await _api.As("RelocationAgent");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => ra.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/questionnaires", Body(), Json)));

        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        (await Ok<List<QuestionnaireDto>>(await ra.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/questionnaires"))).Select(q => q.Number).Should().OnlyHaveUniqueItems().And.HaveCount(10);
    }

    [PgFact]
    public async Task AT_RA_40_Questionnaire_input_is_checked()
    {
        var parcel = await NewParcel();
        var ra = await _api.As("RelocationAgent");
        var url = $"/api/v1/parcels/{parcel.Id}/questionnaires";
        Task<HttpStatusCode> Post(string key, object? value) => Status(ra.Http.PostAsJsonAsync(url, Body(new() { [key] = value }), Json));

        (await Post("relocationType", "")).Should().Be(HttpStatusCode.BadRequest);
        (await Post("relocationType", null)).Should().Be(HttpStatusCode.BadRequest);
        (await Post("relocationType", new string('r', 101))).Should().Be(HttpStatusCode.BadRequest);
        (await Post("interests", new[] { new { name = "" } })).Should().Be(HttpStatusCode.BadRequest);
        (await Post("questions", new[] { new { question = " " } })).Should().Be(HttpStatusCode.BadRequest);
        (await Post("questions", new[] { new { question = "Q", answer = new string('a', 4001) } })).Should().Be(HttpStatusCode.BadRequest);
        (await Post("questions", Enumerable.Range(0, 51).Select(i => new { question = $"Q{i}" }).ToArray())).Should().Be(HttpStatusCode.BadRequest);
        (await Status(ra.Http.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/questionnaires"))).Should().Be(HttpStatusCode.NotFound);
        (await Status(ra.Http.PutAsJsonAsync($"{url}/{Guid.NewGuid()}", Body(), Json))).Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_questionnaires where parcel_id = @p", ("p", parcel.Id))).Should().Be(0, "a refused save creates nothing");
    }

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task AT_X_53_Each_role_can_do_exactly_what_the_Questionnaire_page_allows(string role)
    {
        var parcel = await NewParcel();
        var admin = await _api.As("Admin");
        var url = $"/api/v1/parcels/{parcel.Id}/questionnaires";
        var seed = await Ok<QuestionnaireDto>(await admin.Http.PostAsJsonAsync(url, Body(), Json), HttpStatusCode.Created);
        var doomed = await Ok<QuestionnaireDto>(await admin.Http.PostAsJsonAsync(url, Body(), Json), HttpStatusCode.Created);
        var user = await _api.As(role);
        bool Can(string action) => Spec.Can(role, "parcelquestionnaire", action);
        HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;

        (await Status(user.Http.GetAsync(url))).Should().Be(Expect(Can("read"), HttpStatusCode.OK));
        (await Status(user.Http.PostAsJsonAsync(url, Body(), Json))).Should().Be(Expect(Can("create"), HttpStatusCode.Created));
        (await Status(user.Http.PutAsJsonAsync($"{url}/{seed.Id}", Body(new() { ["dueByDate"] = "2026-09-01" }), Json))).Should().Be(Expect(Can("update"), HttpStatusCode.OK));
        (await Status(user.Http.DeleteAsync($"{url}/{doomed.Id}"))).Should().Be(Expect(Can("delete"), HttpStatusCode.NoContent));
    }

    public static IEnumerable<object[]> Roles() => Spec.Roles.Select(r => new object[] { r });

    [PgFact]
    public async Task Without_signing_in_the_questionnaires_are_not_available()
    {
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/questionnaires")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
