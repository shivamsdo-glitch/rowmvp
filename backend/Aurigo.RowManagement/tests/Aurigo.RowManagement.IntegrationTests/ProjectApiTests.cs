using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.Domain.Common;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

public class ProjectApiTests : IClassFixture<RowApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly RowApiFactory _factory;
    public ProjectApiTests(RowApiFactory factory) => _factory = factory;

    private async Task<HttpClient> As(string role) =>
        _factory.ClientWithToken(await _factory.DevTokenAsync(_factory.CreateClient(), role));

    [Fact]
    public async Task ROW_001_076_238_A_project_goes_from_created_to_unlocked_through_the_api()
    {
        var pm = await As(RoleNames.ProjectManager);

        var create = await pm.PostAsJsonAsync("/api/v1/projects", new
        {
            name = "API Test Highway", region = "North", district = "D9", oversightType = "Federal",
            fundingSource = "FHWA", designEstimate = 1_000_000m,
        }, Json);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var project = (await create.Content.ReadFromJsonAsync<ProjectDto>(Json))!;
        project.Number.Should().MatchRegex(@"^\d{4}-\d{4}$");
        project.AcquisitionGate.Unlocked.Should().BeFalse();

        (await pm.PutAsJsonAsync($"/api/v1/projects/{project.Id}/nepa",
            new { decisionType = "CategoricalExclusion", decisionDate = "2026-01-10" }, Json)).EnsureSuccessStatusCode();
        (await pm.PostAsJsonAsync($"/api/v1/projects/{project.Id}/authorizations", new
        {
            type = "Acquisition", authorizedAmount = 500_000m, federalSharePercent = 80m, authorizedOn = "2026-02-01", programmingConfirmed = true,
        }, Json)).StatusCode.Should().Be(HttpStatusCode.Created);

        var gate = await pm.GetFromJsonAsync<AcquisitionGateDto>($"/api/v1/projects/{project.Id}/acquisition-gate", Json);
        gate!.Unlocked.Should().BeTrue();

        var estimate = await pm.PostAsJsonAsync($"/api/v1/projects/{project.Id}/cost-estimates",
            new { landCost = 100m, improvementsCost = 10m, relocationCost = 5m, indirectCost = 1m }, Json);
        (await estimate.Content.ReadFromJsonAsync<CostEstimateDto>(Json))!.Total.Should().Be(116m);

        var list = await pm.GetFromJsonAsync<PagedResult<ProjectSummaryDto>>("/api/v1/projects?search=API%20Test", Json);
        list!.Items.Should().ContainSingle(p => p.Id == project.Id && p.CurrentEstimate == 116m);
    }

    [Fact]
    public async Task ROW_001_Bad_input_is_a_400_validation_problem_and_duplicates_are_409()
    {
        var pm = await As(RoleNames.ProjectManager);

        var bad = await pm.PostAsJsonAsync("/api/v1/projects", new { name = "", oversightType = "Local" }, Json);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = new { number = "API-DUP-1", name = "Dup", oversightType = "Local" };
        (await pm.PostAsJsonAsync("/api/v1/projects", body, Json)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await pm.PostAsJsonAsync("/api/v1/projects", body, Json)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Roles_without_project_access_are_refused_and_anonymous_callers_are_401()
    {
        var finance = await As(RoleNames.Finance);
        (await finance.GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await finance.PostAsJsonAsync("/api/v1/projects", new { name = "x", oversightType = "Local" }, Json))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var pm = await As(RoleNames.ProjectManager);
        (await pm.PutAsJsonAsync("/api/v1/projects/number-settings", new { pattern = "{FY}-{SEQ:4}", fiscalYearStartMonth = 1 }, Json))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "only Admin can change the format");

        (await _factory.CreateClient().GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ROW_001_Import_reports_each_row()
    {
        var pm = await As(RoleNames.ProjectManager);

        var response = await pm.PostAsJsonAsync("/api/v1/projects/import", new
        {
            rows = new object[]
            {
                new { number = "API-IMP-1", name = "Imported", oversightType = "State" },
                new { number = "API-IMP-1", name = "Same number again", oversightType = "State" },
            },
        }, Json);

        response.EnsureSuccessStatusCode();
        var summary = (await response.Content.ReadFromJsonAsync<ProjectImportSummaryDto>(Json))!;
        summary.Created.Should().Be(1);
        summary.Failed.Should().Be(1);
    }

    [Fact]
    public async Task The_demo_projects_are_seeded_for_the_demo_tenant()
    {
        var pm = await As(RoleNames.ProjectManager);

        var list = await pm.GetFromJsonAsync<PagedResult<ProjectSummaryDto>>("/api/v1/projects?search=DEMO-", Json);

        list!.Items.Select(p => p.Number).Should().Contain(new[] { "DEMO-001", "DEMO-002", "DEMO-003" });
        list.Items.Single(p => p.Number == "DEMO-001").AcquisitionGate.Unlocked.Should().BeFalse();
        list.Items.Single(p => p.Number == "DEMO-002").AcquisitionGate.Unlocked.Should().BeTrue();
        list.Items.Single(p => p.Number == "DEMO-002").CurrentEstimate.Should().Be(6_400_000m);
        list.Items.Single(p => p.Number == "DEMO-003").AcquisitionGate.Reason.Should().Be("UngatedFunding");
    }
}
