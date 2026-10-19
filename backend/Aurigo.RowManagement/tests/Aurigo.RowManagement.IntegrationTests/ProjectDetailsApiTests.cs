using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>
/// Phase 1.5 slice 4 (ADR 0016): the ROW project details fields, the link to a Planned or Build project, and the two lists the form
/// picks from (managers, catalog), on the real API and real Postgres.
/// </summary>
public class ProjectDetailsApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly PgApi _api;
    public ProjectDetailsApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<string> Title(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString()!;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static Dictionary<string, object?> Body(string name, Dictionary<string, object?>? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name, ["oversightType"] = "Federal", ["region"] = "North", ["district"] = "D1",
        };
        if (extra is not null) foreach (var kv in extra) body[kv.Key] = kv.Value;
        return body;
    }

    private async Task<Guid> ManagerIdAsync(TestUser admin, string username = "rowprojectmanager")
    {
        var users = await Ok<List<SettingsUserRow>>(await admin.Http.GetAsync("/api/v1/users"));
        return users.Single(u => u.Username == username).Id;
    }

    private sealed record SettingsUserRow(Guid Id, string Username);

    [PgFact]
    public async Task AT_PM_40_A_project_manager_saves_every_project_details_field_and_reads_it_back()
    {
        var admin = await _api.As("Admin");
        var pm = await _api.As("ProjectManager");
        var managerId = await ManagerIdAsync(admin);

        var created = await Ok<ProjectDto>(await pm.Http.PostAsJsonAsync("/api/v1/projects", Body($"Details {Tag()}", new()
        {
            ["phase"] = "  Phase 1 ", ["location"] = "Premium Residential and Commercial Hub", ["projectManagerUserId"] = managerId,
            ["documentFolderStructure"] = "TEST", ["documentProperties"] = "MTRDOCM", ["constructionProjectManager"] = "ritz",
            ["lettingDate"] = "2026-05-01", ["rightOfWayClearDate"] = "2027-05-31",
            ["constructionStartDate"] = "2026-05-01", ["constructionEndDate"] = "2028-05-23",
            ["linkedSource"] = "Build", ["linkedProjectNumber"] = "bld-2025-017",
        }), Json), HttpStatusCode.Created);

        created.Phase.Should().Be("Phase 1");
        created.Location.Should().Be("Premium Residential and Commercial Hub");
        created.ProjectManagerUserId.Should().Be(managerId);
        created.ProjectManagerName.Should().Be("ROW Project Manager");
        created.DocumentFolderStructure.Should().Be("TEST");
        created.DocumentProperties.Should().Be("MTRDOCM");
        created.ConstructionProjectManager.Should().Be("ritz");
        created.RightOfWayClearDate.Should().Be(new DateOnly(2027, 5, 31));
        created.LinkedSource.Should().Be("Build");
        created.LinkedProjectNumber.Should().Be("BLD-2025-017", "the catalog's spelling wins");
        created.LinkedProjectName.Should().Be("Harbor Bridge Replacement");

        var read = await Ok<ProjectDto>(await pm.Http.GetAsync($"/api/v1/projects/{created.Id}"));
        read.Should().BeEquivalentTo(created, o => o.Excluding(p => p.UpdatedAt));
    }

    [PgFact]
    public async Task AT_PM_40_The_funding_block_is_the_budgets_added_up_the_latest_estimate_and_an_actual_cost_of_zero()
    {
        var pm = await _api.As("ProjectManager");
        var project = await Ok<ProjectDto>(await pm.Http.PostAsJsonAsync("/api/v1/projects", Body($"Funds {Tag()}"), Json), HttpStatusCode.Created);
        project.BoardApprovedFunds.Should().Be(0m);

        (await pm.Http.PutAsJsonAsync($"/api/v1/projects/{project.Id}/budgets/2026", new { amount = 300_000m, notes = (string?)null })).EnsureSuccessStatusCode();
        (await pm.Http.PutAsJsonAsync($"/api/v1/projects/{project.Id}/budgets/2027", new { amount = 177_770.44m, notes = (string?)null })).EnsureSuccessStatusCode();
        (await pm.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/cost-estimates",
            new { landCost = 400_000m, improvementsCost = 50_000m, relocationCost = 20_000m, indirectCost = 4_776.99m, notes = (string?)null })).EnsureSuccessStatusCode();

        var read = await Ok<ProjectDto>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}"));
        read.BoardApprovedFunds.Should().Be(477_770.44m);
        read.CurrentEstimate.Should().Be(474_776.99m);
        read.ActualCost.Should().Be(0m, "payments are not built yet");
    }

    [PgFact]
    public async Task AT_PM_40_Updating_replaces_the_details_and_clearing_the_manager_or_link_clears_them()
    {
        var admin = await _api.As("Admin");
        var pm = await _api.As("ProjectManager");
        var managerId = await ManagerIdAsync(admin, "rowdesign");
        var project = await Ok<ProjectDto>(await pm.Http.PostAsJsonAsync("/api/v1/projects", Body($"Replace {Tag()}", new()
        {
            ["projectManagerUserId"] = managerId, ["linkedSource"] = "Planned", ["linkedProjectNumber"] = "PLN-2026-001", ["phase"] = "Phase 2",
        }), Json), HttpStatusCode.Created);
        project.ProjectManagerName.Should().Be("ROW Designer");
        project.LinkedProjectName.Should().Be("Harbor Corridor Improvements");

        var cleared = await Ok<ProjectDto>(await pm.Http.PutAsJsonAsync($"/api/v1/projects/{project.Id}", Body(project.Name, new() { ["number"] = project.Number }), Json));
        cleared.ProjectManagerUserId.Should().BeNull();
        cleared.ProjectManagerName.Should().BeNull();
        cleared.LinkedSource.Should().BeNull();
        cleared.LinkedProjectNumber.Should().BeNull();
        cleared.Phase.Should().BeNull();

        var relinked = await Ok<ProjectDto>(await pm.Http.PutAsJsonAsync($"/api/v1/projects/{project.Id}",
            Body(project.Name, new() { ["number"] = project.Number, ["linkedSource"] = "Build", ["linkedProjectNumber"] = "BLD-2025-021" }), Json));
        relinked.LinkedProjectName.Should().Be("Pier Lane Reconstruction");
    }

    [PgFact]
    public async Task AT_PM_40_A_manager_who_is_not_in_the_agency_and_a_project_that_the_catalog_lacks_are_refused_with_the_reason()
    {
        var pm = await _api.As("ProjectManager");

        var noManager = await pm.Http.PostAsJsonAsync("/api/v1/projects", Body($"Bad mgr {Tag()}", new() { ["projectManagerUserId"] = Guid.NewGuid() }), Json);
        noManager.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(noManager)).Should().Be("unknown_project_manager");

        var noProject = await pm.Http.PostAsJsonAsync("/api/v1/projects", Body($"Bad link {Tag()}", new() { ["linkedSource"] = "Build", ["linkedProjectNumber"] = "BLD-0000-000" }), Json);
        noProject.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(noProject)).Should().Be("unknown_linked_project");

        // A Planned number is not a Build number.
        (await Title(await pm.Http.PostAsJsonAsync("/api/v1/projects", Body($"Wrong source {Tag()}", new() { ["linkedSource"] = "Build", ["linkedProjectNumber"] = "PLN-2026-001" }), Json)))
            .Should().Be("unknown_linked_project");
        foreach (var bad in new[]
        {
            new Dictionary<string, object?> { ["linkedSource"] = "Other", ["linkedProjectNumber"] = "X" },
            new Dictionary<string, object?> { ["linkedSource"] = "Build" },
            new Dictionary<string, object?> { ["linkedProjectNumber"] = "BLD-2025-017" },
            new Dictionary<string, object?> { ["phase"] = new string('p', 101) },
            new Dictionary<string, object?> { ["location"] = new string('l', 501) },
        })
            (await pm.Http.PostAsJsonAsync("/api/v1/projects", Body($"Bad {Tag()}", bad), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest, JsonSerializer.Serialize(bad));
    }

    [PgTheory]
    [MemberData(nameof(EachRole))]
    public async Task AT_PM_41_The_managers_list_and_the_catalog_follow_the_Projects_read_permission_for_every_role(string role)
    {
        var user = await _api.As(role);
        var allowed = Spec.Can(role, "projects", "read");

        var managers = await user.Http.GetAsync("/api/v1/projects/managers");
        var planned = await user.Http.GetAsync("/api/v1/projects/catalog?source=Planned&search=harbor");
        managers.StatusCode.Should().Be(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        planned.StatusCode.Should().Be(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        if (!allowed) return;

        (await managers.Content.ReadFromJsonAsync<List<AssignableManagerDto>>(Json))!.Select(m => m.Name).Should().Contain(new[] { "ROW Project Manager", "ROW Designer" });
        var rows = await planned.Content.ReadFromJsonAsync<List<CatalogProjectDto>>(Json);
        rows.Should().ContainSingle().Which.Should().Be(new CatalogProjectDto("Planned", "PLN-2026-001", "Harbor Corridor Improvements"));
    }

    public static IEnumerable<object[]> EachRole() => Spec.EachRole();

    [PgFact]
    public async Task AT_PM_41_The_catalog_filters_by_source_and_text_and_refuses_an_unknown_source()
    {
        var pm = await _api.As("ProjectManager");

        (await Ok<List<CatalogProjectDto>>(await pm.Http.GetAsync("/api/v1/projects/catalog?source=Build"))).Select(p => p.Number)
            .Should().Equal("BLD-2025-017", "BLD-2025-021", "BLD-2026-003");
        (await Ok<List<CatalogProjectDto>>(await pm.Http.GetAsync("/api/v1/projects/catalog?source=Planned&search=sr-20"))).Should().ContainSingle().Which.Number.Should().Be("PLN-2027-004");
        (await Ok<List<CatalogProjectDto>>(await pm.Http.GetAsync("/api/v1/projects/catalog?source=Planned&search=zzz"))).Should().BeEmpty();
        (await pm.Http.GetAsync("/api/v1/projects/catalog?source=Wrong")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync("/api/v1/projects/catalog")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _api.Factory.CreateClient().GetAsync("/api/v1/projects/catalog?source=Build")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _api.Factory.CreateClient().GetAsync("/api/v1/projects/managers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [PgFact]
    public async Task AT_PM_41_Deactivated_people_are_not_offered_as_managers_and_cannot_be_chosen()
    {
        var admin = await _api.As("Admin");
        var pm = await _api.As("ProjectManager");
        var role = (await Ok<List<Application.Settings.RoleDto>>(await admin.Http.GetAsync("/api/v1/roles"))).Single(r => r.Name == "Finance");
        var username = $"gone{Tag()}";
        var user = await Ok<Application.Settings.SettingsUserDto>(await admin.Http.PostAsJsonAsync("/api/v1/users",
            new { username, firstName = "Gone", lastName = Tag(), email = $"{username}@example.test", password = "Passw0rd-abc", roleId = role.Id }, Json), HttpStatusCode.Created);
        (await Ok<List<AssignableManagerDto>>(await pm.Http.GetAsync("/api/v1/projects/managers"))).Should().Contain(m => m.Id == user.Id);

        await Ok<Application.Settings.SettingsUserDto>(await admin.Http.PutAsJsonAsync($"/api/v1/users/{user.Id}",
            new { firstName = "Gone", lastName = user.LastName, email = user.Email, roleId = role.Id, isActive = false }, Json));

        (await Ok<List<AssignableManagerDto>>(await pm.Http.GetAsync("/api/v1/projects/managers"))).Should().NotContain(m => m.Id == user.Id);
        (await Title(await pm.Http.PostAsJsonAsync("/api/v1/projects", Body($"Inactive mgr {Tag()}", new() { ["projectManagerUserId"] = user.Id }), Json)))
            .Should().Be("unknown_project_manager");
    }
}
