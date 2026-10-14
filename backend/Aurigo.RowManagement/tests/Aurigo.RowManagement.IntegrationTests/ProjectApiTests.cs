using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>
/// Project core on the real API and real Postgres (ROW-001, 003, 004, 012, 013, 076, 238): the job, every role in both
/// directions, races, tenant isolation, audit, schema and hostile input. Matches acceptance cases AT-PM-20, AT-AD-25.
/// </summary>
public class ProjectApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Guid OtherTenant = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private readonly PgApi _api;
    public ProjectApiTests(PgApi api) => _api = api;

    private static object Body(string name, string? number = null, string oversight = "Federal") => new
    {
        number, name, region = "North", district = "D9", oversightType = oversight, fundingSource = "FHWA", designEstimate = 1_000_000m,
    };

    private static async Task<T> Read<T>(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<T>(Json))!;

    private async Task<ProjectDto> CreateAs(TestUser user, string name, string? number = null, string oversight = "Federal")
    {
        var res = await user.Http.PostAsJsonAsync("/api/v1/projects", Body(name, number, oversight), Json);
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return await Read<ProjectDto>(res);
    }

    private async Task EnsureOtherTenantAsync()
    {
        using var scope = _api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();
        if (!await db.Tenants.AnyAsync(t => t.Id == OtherTenant))
        {
            db.Tenants.Add(new Tenant { Id = OtherTenant, Name = "Other Agency", Slug = "other-c3", IsActive = true });
            await db.SaveChangesAsync();
        }
        await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().EnsureDefaultRolesAsync(OtherTenant);
    }

    // ── the job (AT-PM-20) ──

    [PgFact]
    public async Task AT_PM_20_A_project_goes_from_created_to_cleared_to_acquire_through_the_api()
    {
        var pm = await _api.As("ProjectManager");
        var project = await CreateAs(pm, "Journey Highway");
        project.Number.Should().MatchRegex(@"^\d{4}-\d{4}$");
        project.AcquisitionGate.Should().Match<AcquisitionGateDto>(g => !g.Unlocked && g.Missing.Count == 2);

        (await pm.Http.PutAsJsonAsync($"/api/v1/projects/{project.Id}/nepa",
            new { decisionType = "CategoricalExclusion", decisionDate = "2026-01-10" }, Json)).EnsureSuccessStatusCode();
        var preliminary = await pm.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/authorizations", new
        {
            type = "Preliminary", authorizedAmount = 100_000m, federalSharePercent = 80m, authorizedOn = "2026-01-20", programmingConfirmed = false,
        }, Json);
        preliminary.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Read<AcquisitionGateDto>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}/acquisition-gate")))
            .Unlocked.Should().BeFalse("a preliminary authorization covers appraisal and title only");

        var acquisition = await pm.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/authorizations", new
        {
            type = "Acquisition", authorizedAmount = 500_000m, federalSharePercent = 80m, authorizedOn = "2026-02-01", programmingConfirmed = true,
        }, Json);
        var acquisitionDto = await Read<RowAuthorizationDto>(acquisition);
        acquisitionDto.FederalShareAmount.Should().Be(400_000m);
        (await Read<AcquisitionGateDto>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}/acquisition-gate")))
            .Should().Match<AcquisitionGateDto>(g => g.Unlocked && g.Reason == "ClearanceComplete");

        // Taking the authorization away holds the project again.
        (await pm.Http.DeleteAsync($"/api/v1/projects/{project.Id}/authorizations/{acquisitionDto.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Read<AcquisitionGateDto>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}/acquisition-gate"))).Unlocked.Should().BeFalse();
    }

    [PgFact]
    public async Task AT_PM_20_Estimates_keep_every_version_and_budgets_are_one_per_year()
    {
        var pm = await _api.As("ProjectManager");
        var project = await CreateAs(pm, "Money Highway");

        var v1 = await Read<CostEstimateDto>(await pm.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/cost-estimates",
            new { landCost = 100m, improvementsCost = 10m, relocationCost = 5m, indirectCost = 1m, notes = "first" }, Json));
        var v2 = await Read<CostEstimateDto>(await pm.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/cost-estimates",
            new { landCost = 200m, improvementsCost = 10m, relocationCost = 5m, indirectCost = 1m }, Json));
        v1.Version.Should().Be(1);
        v1.Total.Should().Be(116m);
        v2.Version.Should().Be(2);

        var history = await Read<List<CostEstimateDto>>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}/cost-estimates"));
        history.Select(e => e.Version).Should().Equal(2, 1);
        (await Read<ProjectDto>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}"))).CurrentEstimate.Should().Be(216m);

        (await pm.Http.PutAsJsonAsync($"/api/v1/projects/{project.Id}/budgets/2027", new { amount = 1000m }, Json)).EnsureSuccessStatusCode();
        (await pm.Http.PutAsJsonAsync($"/api/v1/projects/{project.Id}/budgets/2027", new { amount = 1500m, notes = "adjusted" }, Json)).EnsureSuccessStatusCode();
        var budgets = await Read<List<BudgetDto>>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}/budgets"));
        budgets.Should().ContainSingle().Which.Amount.Should().Be(1500m);
        (await pm.Http.DeleteAsync($"/api/v1/projects/{project.Id}/budgets/2027")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Read<List<BudgetDto>>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}/budgets"))).Should().BeEmpty();

        // The stored data is what the screens show: both versions are rows, the totals are stored.
        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from project_cost_estimates where project_id = @p", ("p", project.Id))).Should().Be(2);
    }

    [PgFact]
    public async Task AT_PM_20_An_approved_exception_unlocks_and_is_recorded_with_its_reason()
    {
        var pm = await _api.As("ProjectManager");
        var project = await CreateAs(pm, "Hardship Highway");

        var missingReason = await pm.Http.PutAsJsonAsync($"/api/v1/projects/{project.Id}/acquisition-exception",
            new { exceptionType = "Hardship", approvedOn = "2026-03-01" }, Json);
        missingReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await pm.Http.PutAsJsonAsync($"/api/v1/projects/{project.Id}/acquisition-exception",
            new { exceptionType = "Hardship", approvedOn = "2026-03-01", reason = "Owner medical hardship" }, Json);
        var updated = await Read<ProjectDto>(ok);
        updated.AcquisitionGate.Unlocked.Should().BeTrue();
        updated.ExceptionReason.Should().Be("Owner medical hardship");
    }

    [PgFact]
    public async Task ROW_076_Local_funds_are_never_held()
    {
        var pm = await _api.As("ProjectManager");
        (await CreateAs(pm, "Sidewalk", oversight: "Local")).AcquisitionGate.Unlocked.Should().BeTrue();
    }

    // ── numbering ──

    [PgFact]
    public async Task ROW_001_Twenty_simultaneous_creates_get_twenty_different_numbers_with_no_errors()
    {
        var pm = await _api.As("ProjectManager");
        var before = await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from projects");

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
            await pm.Http.PostAsJsonAsync("/api/v1/projects", Body($"Parallel {i}"), Json)));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created,
            "a lost race for a generated number is retried inside the server, never shown to the person");
        var numbers = new List<string>();
        foreach (var r in results) numbers.Add((await Read<ProjectDto>(r)).Number);
        numbers.Distinct().Should().HaveCount(20);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from projects")).Should().Be(before + 20);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from (select distinct tenant_id, number from projects) x")).Should().Be(before + 20);
    }

    [PgFact]
    public async Task ROW_001_Ten_people_typing_the_same_number_at_once_produce_exactly_one_project()
    {
        var pm = await _api.As("ProjectManager");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async i =>
            await pm.Http.PostAsJsonAsync("/api/v1/projects", Body($"Same number {i}", "RACE-1"), Json)));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(9);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from projects where number = 'RACE-1'")).Should().Be(1);
    }

    [PgFact]
    public async Task ROW_001_The_admin_changes_the_format_and_later_projects_follow_it_but_old_numbers_stay()
    {
        await EnsureOtherTenantAsync();
        var admin = await _api.As("Admin", OtherTenant);
        var pm = await _api.As("ProjectManager", OtherTenant);
        var old = await CreateAs(pm, "Before the change");

        var put = await admin.Http.PutAsJsonAsync("/api/v1/projects/number-settings", new { pattern = "DOT-{YY}-{SEQ:3}", fiscalYearStartMonth = 1 }, Json);
        put.EnsureSuccessStatusCode();
        var settings = await Read<ProjectNumberSettingsDto>(put);
        settings.Example.Should().MatchRegex(@"^DOT-\d{2}-002$", "the sequence continues within the fiscal year, so the next number is 002");

        var next = await CreateAs(pm, "After the change");
        next.Number.Should().Be(settings.Example);
        (await Read<ProjectDto>(await pm.Http.GetAsync($"/api/v1/projects/{old.Id}"))).Number.Should().Be(old.Number);

        var bad = await admin.Http.PutAsJsonAsync("/api/v1/projects/number-settings", new { pattern = "{FY}-", fiscalYearStartMonth = 1 }, Json);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [PgFact]
    public async Task ROW_001_Import_creates_good_rows_reports_bad_ones_and_keeps_going()
    {
        var pm = await _api.As("ProjectManager");
        await CreateAs(pm, "Already here", "IMP-EXIST");

        var res = await pm.Http.PostAsJsonAsync("/api/v1/projects/import", new
        {
            rows = new object[]
            {
                new { number = "IMP-A", name = "Imported A", oversightType = "State" },
                new { number = "IMP-EXIST", name = "Clash", oversightType = "State" },
                new { name = "Generated", oversightType = "Local" },
                new { number = "IMP-B", name = " ", oversightType = "Local" },
            },
        }, Json);

        var summary = await Read<ProjectImportSummaryDto>(res);
        summary.Created.Should().Be(2);
        summary.Failed.Should().Be(2);
        summary.Results.Select(r => r.Outcome).Should().Equal("Created", "Failed", "Created", "Failed");
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from projects where number in ('IMP-A','IMP-B')")).Should().Be(1);
    }

    // ── every role, both directions ──

    [PgTheory]
    [MemberData(nameof(Spec.EachRole), MemberType = typeof(Spec))]
    public async Task Every_role_can_do_exactly_what_the_spec_allows_on_projects(string role)
    {
        var admin = await _api.As("Admin");
        var seeded = await CreateAs(admin, $"Spec target for {role}");
        var user = await _api.As(role);

        HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;
        bool Can(string action) => Spec.Can(role, "projects", action);

        (await user.Http.GetAsync("/api/v1/projects")).StatusCode.Should().Be(Expect(Can("read"), HttpStatusCode.OK), $"{role} list");
        (await user.Http.GetAsync($"/api/v1/projects/{seeded.Id}")).StatusCode.Should().Be(Expect(Can("read"), HttpStatusCode.OK), $"{role} open");
        (await user.Http.GetAsync($"/api/v1/projects/{seeded.Id}/cost-estimates")).StatusCode.Should().Be(Expect(Can("read"), HttpStatusCode.OK));
        (await user.Http.GetAsync($"/api/v1/projects/{seeded.Id}/authorizations")).StatusCode.Should().Be(Expect(Can("read"), HttpStatusCode.OK));
        (await user.Http.GetAsync($"/api/v1/projects/{seeded.Id}/acquisition-gate")).StatusCode.Should().Be(Expect(Can("read"), HttpStatusCode.OK));

        (await user.Http.PostAsJsonAsync("/api/v1/projects", Body($"By {role}"), Json)).StatusCode
            .Should().Be(Expect(Can("create"), HttpStatusCode.Created), $"{role} create");
        (await user.Http.PutAsJsonAsync($"/api/v1/projects/{seeded.Id}", Body($"Renamed by {role}"), Json)).StatusCode
            .Should().Be(Expect(Can("update"), HttpStatusCode.OK), $"{role} update");
        (await user.Http.PostAsJsonAsync($"/api/v1/projects/{seeded.Id}/cost-estimates",
            new { landCost = 1m, improvementsCost = 0m, relocationCost = 0m, indirectCost = 0m }, Json)).StatusCode
            .Should().Be(Expect(Can("update"), HttpStatusCode.Created), $"{role} add estimate");
        (await user.Http.PutAsJsonAsync($"/api/v1/projects/{seeded.Id}/nepa", new { decisionType = "CategoricalExclusion", decisionDate = "2026-01-01" }, Json))
            .StatusCode.Should().Be(Expect(Can("update"), HttpStatusCode.OK), $"{role} record NEPA");
        (await user.Http.PostAsJsonAsync("/api/v1/projects/import",
            new { rows = new object[] { new { name = $"Import by {role}", oversightType = "Local" } } }, Json)).StatusCode
            .Should().Be(Expect(Can("create"), HttpStatusCode.OK), $"{role} import");

        // Deleting a budget needs Delete, which is stricter than the Update that sets it.
        await admin.Http.PutAsJsonAsync($"/api/v1/projects/{seeded.Id}/budgets/2030", new { amount = 1m }, Json);
        (await user.Http.DeleteAsync($"/api/v1/projects/{seeded.Id}/budgets/2030")).StatusCode
            .Should().Be(Expect(Can("delete"), HttpStatusCode.NoContent), $"{role} delete budget");

        // The number format is configuration: administrators only.
        var format = await user.Http.PutAsJsonAsync("/api/v1/projects/number-settings", new { pattern = "{FY}-{SEQ:4}", fiscalYearStartMonth = 1 }, Json);
        format.StatusCode.Should().Be(Expect(Spec.Can(role, "administration", "update"), HttpStatusCode.OK), $"{role} number format");
    }

    [PgFact]
    public async Task Anonymous_callers_are_refused_everywhere_on_projects()
    {
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync("/api/v1/projects", Body("x"), Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/acquisition-gate")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── tenancy ──

    [PgFact]
    public async Task One_agency_never_sees_or_changes_another_agencys_projects()
    {
        await EnsureOtherTenantAsync();
        var mine = await _api.As("ProjectManager");
        var theirs = await _api.As("ProjectManager", OtherTenant);
        var secret = await CreateAs(mine, "Secret corridor", "TENANT-SECRET");

        (await theirs.Http.GetAsync($"/api/v1/projects/{secret.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PutAsJsonAsync($"/api/v1/projects/{secret.Id}", Body("Hijacked"), Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PostAsJsonAsync($"/api/v1/projects/{secret.Id}/cost-estimates",
            new { landCost = 1m, improvementsCost = 0m, relocationCost = 0m, indirectCost = 0m }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var list = await Read<PagedResult<ProjectSummaryDto>>(await theirs.Http.GetAsync("/api/v1/projects?search=TENANT-SECRET"));
        list.Items.Should().BeEmpty();

        // The same number can exist in each agency.
        (await theirs.Http.PostAsJsonAsync("/api/v1/projects", Body("Their own", "TENANT-SECRET"), Json)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from projects where number = 'TENANT-SECRET'")).Should().Be(2);
    }

    // ── schema, audit, hostile input ──

    [PgFact]
    public async Task The_schema_enforces_unique_numbers_and_versions_and_the_audit_log_records_creation()
    {
        var pm = await _api.As("ProjectManager", user: Guid.NewGuid());
        var project = await CreateAs(pm, "Audited");

        var indexes = (await PgTestDatabase.QueryAsync(_api.Database,
            "select indexname from pg_indexes where tablename in ('projects','project_cost_estimates','project_budgets','project_number_counters','project_number_settings')"))
            .Select(r => (string)r[0]!).ToList();
        indexes.Should().Contain(i => i.Contains("tenant_id") && i.Contains("number"));
        indexes.Should().Contain(i => i.Contains("project_id") && i.Contains("version"));
        indexes.Should().Contain(i => i.Contains("project_id") && i.Contains("fiscal_year"));

        // The concurrency token is Postgres's own xmin, not a column we created.
        (await PgTestDatabase.QueryAsync(_api.Database,
            "select 1 from information_schema.columns where table_name='project_number_counters' and column_name='xmin'")).Should().BeEmpty();
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from project_number_counters where xmin::text::bigint > 0")).Should().BeGreaterThan(0);

        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from audit_log where entity_type = 'Project' and entity_id = @id", ("id", project.Id))).Should().BeGreaterThan(0);
    }

    [PgFact]
    public async Task Hostile_and_oversized_input_is_a_clear_400_never_a_500()
    {
        var pm = await _api.As("ProjectManager");
        async Task<HttpStatusCode> Create(object body) => (await pm.Http.PostAsJsonAsync("/api/v1/projects", body, Json)).StatusCode;

        (await Create(new { name = "bad\u0000name", oversightType = "Local" })).Should().Be(HttpStatusCode.BadRequest);
        (await Create(new { name = new string('x', 201), oversightType = "Local" })).Should().Be(HttpStatusCode.BadRequest);
        (await Create(new { name = "ok", oversightType = "Nonsense" })).Should().Be(HttpStatusCode.BadRequest);
        (await Create(new { name = "ok", oversightType = "Local", fundingAmount = -1 })).Should().Be(HttpStatusCode.BadRequest);
        (await Create(new { name = "ok", oversightType = "Local", number = "has space" })).Should().Be(HttpStatusCode.BadRequest);
        (await Create(new { name = "ok", oversightType = "Local", number = "'; drop table projects;--" })).Should().Be(HttpStatusCode.BadRequest);
        (await Create(new { name = "<script>alert(1)</script>", oversightType = "Local" })).Should().Be(HttpStatusCode.Created, "text is stored as data and escaped on display");
        (await Create(new { name = "ok", oversightType = "Local", constructionStartDate = "2027-05-01", constructionEndDate = "2027-04-01" }))
            .Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync("/api/v1/projects?page=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync("/api/v1/projects?pageSize=100000")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync("/api/v1/projects/not-a-guid")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await pm.Http.PostAsJsonAsync("/api/v1/projects/import", new { rows = Array.Empty<object>() }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsync("/api/v1/projects", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync($"/api/v1/projects/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await pm.Http.GetAsync("/api/v1/projects?search=%27%3B%20drop%20table%20projects%3B--")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [PgFact]
    public async Task Ten_estimates_saved_at_once_never_share_a_version_and_never_error()
    {
        var pm = await _api.As("ProjectManager");
        var project = await CreateAs(pm, "Estimate race");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async i =>
            await pm.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/cost-estimates",
                new { landCost = 100m + i, improvementsCost = 0m, relocationCost = 0m, indirectCost = 0m }, Json)));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.Conflict);
        var created = results.Count(r => r.StatusCode == HttpStatusCode.Created);
        created.Should().BeGreaterThan(0);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from project_cost_estimates where project_id=@p", ("p", project.Id))).Should().Be(created);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(distinct version) from project_cost_estimates where project_id=@p", ("p", project.Id))).Should().Be(created);
    }

    [PgFact]
    public async Task The_demo_projects_are_seeded_once_with_their_estimates_budgets_and_authorizations()
    {
        var pm = await _api.As("ProjectManager");

        var list = await Read<PagedResult<ProjectSummaryDto>>(await pm.Http.GetAsync("/api/v1/projects?search=DEMO-"));

        list.Items.Select(p => p.Number).Should().Contain(new[] { "DEMO-001", "DEMO-002", "DEMO-003" });
        list.Items.Single(p => p.Number == "DEMO-001").AcquisitionGate.Unlocked.Should().BeFalse();
        list.Items.Single(p => p.Number == "DEMO-002").AcquisitionGate.Unlocked.Should().BeTrue();
        list.Items.Single(p => p.Number == "DEMO-002").CurrentEstimate.Should().Be(6_400_000m);
        list.Items.Single(p => p.Number == "DEMO-003").AcquisitionGate.Reason.Should().Be("UngatedFunding");
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from projects where number like 'DEMO-%'")).Should().Be(3);
    }
}
