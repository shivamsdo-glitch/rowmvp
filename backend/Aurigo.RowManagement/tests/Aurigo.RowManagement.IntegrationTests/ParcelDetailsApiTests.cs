using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>
/// Phase 1.5 slice 5a (ADR 0017): the fields of the parcel details form (CAD number, acquisition interest type, possession dates,
/// segment, priority, possession type, parcel location, Active Parcel) on the real API and real Postgres.
/// </summary>
public class ParcelDetailsApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly PgApi _api;
    public ParcelDetailsApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<string> Title(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString()!;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task<ProjectDto> NewProjectAsync()
    {
        var pm = await _api.As("ProjectManager");
        return await Ok<ProjectDto>(await pm.Http.PostAsJsonAsync("/api/v1/projects",
            new { name = $"Parcel details {Tag()}", oversightType = "Local", phase = "Phase 1" }), HttpStatusCode.Created);
    }

    private static Dictionary<string, object?> Body(Guid? projectId, Dictionary<string, object?>? extra = null)
    {
        var body = new Dictionary<string, object?> { ["areaSqFt"] = 43560, ["acquisitionType"] = "Standard", ["address"] = "1 Main St" };
        if (projectId is not null) body["projectId"] = projectId;
        if (extra is not null) foreach (var kv in extra) body[kv.Key] = kv.Value;
        return body;
    }

    private static Dictionary<string, object?> Details() => new()
    {
        ["cadNumber"] = "  CAD-77 ", ["acquisitionInterestType"] = "Permanent Easement", ["possessionDate"] = "2026-06-01",
        ["proposedPossessionDate"] = "2026-05-01", ["segment"] = "Seg A", ["priority"] = "High", ["possessionType"] = "PUA",
        ["parcelLocation"] = "North side of Main St", ["isActiveParcel"] = true,
    };

    [PgFact]
    public async Task AT_DE_40_A_designer_saves_every_parcel_details_field_and_reads_it_back()
    {
        var project = await NewProjectAsync();
        var designer = await _api.As("Designer");

        var created = await Ok<ParcelDto>(await designer.Http.PostAsJsonAsync("/api/v1/parcels", Body(project.Id, Details())), HttpStatusCode.Created);
        created.CadNumber.Should().Be("CAD-77", "text is trimmed");
        created.AcquisitionInterestType.Should().Be("Permanent Easement");
        created.PossessionDate.Should().Be(new DateOnly(2026, 6, 1));
        created.ProposedPossessionDate.Should().Be(new DateOnly(2026, 5, 1));
        created.Segment.Should().Be("Seg A");
        created.Priority.Should().Be("High");
        created.PossessionType.Should().Be("PUA");
        created.ParcelLocation.Should().Be("North side of Main St");
        created.IsActiveParcel.Should().BeTrue();

        var again = await Ok<ParcelDto>(await designer.Http.GetAsync($"/api/v1/parcels/{created.Id}"));
        again.CadNumber.Should().Be("CAD-77");
        again.ProposedPossessionDate.Should().Be(new DateOnly(2026, 5, 1));
    }

    [PgFact]
    public async Task AT_DE_40_An_update_changes_and_clears_the_details_and_unticks_Active_Parcel()
    {
        var project = await NewProjectAsync();
        var designer = await _api.As("Designer");
        var created = await Ok<ParcelDto>(await designer.Http.PostAsJsonAsync("/api/v1/parcels", Body(project.Id, Details())), HttpStatusCode.Created);

        var changed = await Ok<ParcelDto>(await designer.Http.PutAsJsonAsync($"/api/v1/parcels/{created.Id}", Body(null, new()
        {
            ["cadNumber"] = "CAD-78", ["segment"] = null, ["priority"] = "", ["isActiveParcel"] = false, ["possessionDate"] = null,
        })));
        changed.CadNumber.Should().Be("CAD-78");
        changed.Segment.Should().BeNull();
        changed.Priority.Should().BeNull("a blank value is stored as nothing");
        changed.PossessionDate.Should().BeNull();
        changed.AcquisitionInterestType.Should().BeNull("an update that leaves a field out clears it, as the form always sends them all");
        changed.IsActiveParcel.Should().BeFalse();
        changed.State.ToString().Should().Be("Active", "the Active Parcel tick is not the split/combine state");
    }

    [PgFact]
    public async Task AT_DE_40_Too_long_values_are_refused_and_older_clients_that_send_none_still_work()
    {
        var project = await NewProjectAsync();
        var designer = await _api.As("Designer");

        foreach (var (field, length) in new[] { ("cadNumber", 101), ("acquisitionInterestType", 101), ("segment", 101), ("priority", 101), ("possessionType", 101), ("parcelLocation", 501) })
        {
            var r = await designer.Http.PostAsJsonAsync("/api/v1/parcels", Body(project.Id, new() { [field] = new string('x', length) }));
            r.StatusCode.Should().Be(HttpStatusCode.BadRequest, field);
        }

        var plain = await Ok<ParcelDto>(await designer.Http.PostAsJsonAsync("/api/v1/parcels", Body(project.Id)), HttpStatusCode.Created);
        plain.IsActiveParcel.Should().BeTrue("a parcel is active unless someone unticks it");
        plain.CadNumber.Should().BeNull();
    }

    [PgTheory]
    [InlineData("Designer", true)]
    [InlineData("ProjectManager", true)]
    [InlineData("DesignApprover", false)]
    [InlineData("Surveyor", false)]
    [InlineData("TitleResearcher", false)]
    [InlineData("Negotiator", false)]
    [InlineData("Finance", false)]
    public async Task AT_DE_41_Only_roles_that_create_parcels_can_add_one_with_details(string role, bool allowed)
    {
        var project = await NewProjectAsync();
        var user = await _api.As(role);

        var r = await user.Http.PostAsJsonAsync("/api/v1/parcels", Body(project.Id, Details()));

        r.StatusCode.Should().Be(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden);
        if (!allowed) (await Title(r)).Should().NotBeNullOrEmpty();
    }

    [PgFact]
    public async Task AT_DE_41_A_design_approver_may_update_the_details_but_not_create_or_delete()
    {
        var project = await NewProjectAsync();
        var designer = await _api.As("Designer");
        var created = await Ok<ParcelDto>(await designer.Http.PostAsJsonAsync("/api/v1/parcels", Body(project.Id)), HttpStatusCode.Created);
        var approver = await _api.As("DesignApprover");

        var ok = await Ok<ParcelDto>(await approver.Http.PutAsJsonAsync($"/api/v1/parcels/{created.Id}", Body(null, new() { ["priority"] = "Urgent" })));
        ok.Priority.Should().Be("Urgent");
        (await approver.Http.PostAsJsonAsync("/api/v1/parcels", Body(project.Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
