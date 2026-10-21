using System.Net;
using System.Net.Http.Json;
using Aurigo.RowManagement.Application.Dashboard;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>ADR 0024: the ROW Director's program view and the project drill-down, on the real API and real Postgres. Matches AT-X-61, AT-X-62.</summary>
public class ProgramDashboardApiTests : IClassFixture<PgApi>
{
    private readonly PgApi _api;
    public ProgramDashboardApiTests(PgApi api) => _api = api;

    private static Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK) => LifecycleSupport.Ok<T>(r, expected);

    private sealed record Scene(Guid ProjectId, string ProjectNumber, ParcelDto A, ParcelDto B, ParcelDto C);

    /// <summary>A project whose clear date has passed and whose estimate is already exceeded, with three parcels at different points.</summary>
    private async Task<Scene> MakeScene()
    {
        var admin = (await _api.As("Admin")).Http;
        var clear = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10).ToString("yyyy-MM-dd");
        var project = await Ok<ProjectDto>(await admin.PostAsJsonAsync("/api/v1/projects", new { name = $"Program {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local", rightOfWayClearDate = clear }, LifecycleSupport.Json), HttpStatusCode.Created);
        (await admin.PostAsJsonAsync($"/api/v1/projects/{project.Id}/cost-estimates", new { landCost = 50_000m, improvementsCost = 0m, relocationCost = 0m, indirectCost = 0m }, LifecycleSupport.Json)).StatusCode.Should().Be(HttpStatusCode.Created);

        async Task<ParcelDto> NewParcel() => await Ok<ParcelDto>(await admin.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 100m, acquisitionType = "Standard", address = $"{Guid.NewGuid().ToString("N")[..5]} Program Rd" }, LifecycleSupport.Json), HttpStatusCode.Created);
        var a = await NewParcel();
        var b = await NewParcel();
        var c = await NewParcel();

        foreach (var step in new[] { "title_survey", "appraisal" }) (await admin.PostAsJsonAsync($"/api/v1/parcels/{a.Id}/advance", new { targetStepKey = step }, LifecycleSupport.Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync($"/api/v1/parcels/{b.Id}/advance", new { targetStepKey = "title_survey" }, LifecycleSupport.Json)).StatusCode.Should().Be(HttpStatusCode.OK);

        // A: an appraisal worth 102,500.25 with 90,000 approved, an agreed amount of 95,000 and an actual closing date, so it is closed.
        var appraisal = await admin.PostAsJsonAsync($"/api/v1/parcels/{a.Id}/appraisals", new Dictionary<string, object?>
        {
            ["appraisalType"] = "Full Appraisal", ["appraiser"] = "Ann Appraiser", ["amounts"] = new[] { new { amountType = "Land", description = (string?)null, amount = 100000m }, new { amountType = "Damages", description = (string?)null, amount = 2500.25m } },
            ["approvedJustCompensation"] = 90000m, ["reviewCompletedDate"] = "2026-02-25", ["reviewSubmittedDate"] = "2026-02-26",
        }, LifecycleSupport.Json);
        appraisal.StatusCode.Should().Be(HttpStatusCode.Created, await appraisal.Content.ReadAsStringAsync());
        var neg = await admin.PutAsJsonAsync($"/api/v1/parcels/{a.Id}/negotiation", new { assignedTo = "Casey", totalAgreementAmount = 95000m }, LifecycleSupport.Json);
        neg.StatusCode.Should().Be(HttpStatusCode.OK, await neg.Content.ReadAsStringAsync());
        var close = await admin.PutAsJsonAsync($"/api/v1/parcels/{a.Id}/title-closing", new { titleCompany = "Lone Star", anticipatedClosingDate = "2026-05-01", actualClosingDate = "2026-05-02" }, LifecycleSupport.Json);
        close.StatusCode.Should().Be(HttpStatusCode.OK, await close.Content.ReadAsStringAsync());
        return new Scene(project.Id, project.Number, a, b, c);
    }

    private async Task<ProgramDashboardDto> Program(string role = "ProjectManager") => await Ok<ProgramDashboardDto>(await (await _api.As(role)).Http.GetAsync("/api/v1/dashboard/program"));

    [PgFact]
    public async Task AT_X_61_Each_project_shows_where_ROW_stands_how_much_is_closed_what_it_costs_and_its_health_with_reasons()
    {
        var s = await MakeScene();
        var program = await Program();

        var row = program.Projects.Should().ContainSingle(p => p.Id == s.ProjectId).Subject;
        (row.Parcels, row.Closed, row.PercentClosed).Should().Be((3, 1, 33));
        row.EstimateTotal.Should().Be(50_000m);
        row.KnownCost.Should().Be(95_000m, "the agreed amount wins over the approved just compensation");
        row.ByStep.Single(x => x.StepKey == "setup").Count.Should().Be(1);
        row.ByStep.Single(x => x.StepKey == "title_survey").Count.Should().Be(1);
        row.ByStep.Single(x => x.StepKey == "appraisal").Count.Should().Be(1);
        row.ByStep.Select(x => x.StepKey).Should().ContainInOrder("setup", "title_survey", "appraisal", "offer", "negotiation", "agreement", "closing");
        row.ByStep.First(x => x.StepKey == "title_survey").Label.Should().Be("Title & survey");
        row.Health.Should().Be("Red");
        row.Reasons.Should().Contain(r => r.Contains("clear date") && r.Contains("has passed")).And.Contain(r => r.Contains("over the estimate"));
        row.DaysToClear.Should().Be(-10);

        program.Tiles.Select(t => t.Key).Should().Contain(new[] { "projects", "parcels", "closed", "attention", "stuck", "estimate", "known" });
        program.Tiles.Single(t => t.Key == "projects").Value.Should().BeGreaterThan(0);
        program.Tiles.Single(t => t.Key == "attention").Value.Should().BeGreaterThan(0);
        program.Projects.Select(p => p.Number).Should().BeInAscendingOrder();
    }

    [PgFact]
    public async Task AT_X_61_Time_in_each_step_is_measured_from_the_real_moves_and_is_ranked_by_team()
    {
        await MakeScene();
        var program = await Program("Admin");

        program.Stages.Select(x => x.StepKey).Should().ContainInOrder("setup", "title_survey", "appraisal", "offer", "negotiation", "agreement", "closing");
        var title = program.Stages.Single(x => x.StepKey == "title_survey");
        title.Team.Should().Be("Title & Survey");
        title.CompletedStays.Should().BeGreaterThan(0, "parcel A passed through title and survey");
        title.ParcelsNow.Should().BeGreaterThan(0, "parcel B is on it");
        program.Stages.Single(x => x.StepKey == "appraisal").Team.Should().Be("Appraisal");
        program.Stages.Single(x => x.StepKey == "negotiation").Team.Should().Be("Negotiation");

        program.Teams.Select(t => t.Team).Should().Contain(new[] { "Project team", "Title & Survey", "Appraisal" });
        program.Teams.Select(t => t.AvgDaysPerStay ?? 0).Should().BeInDescendingOrder();
        program.Teams.Should().OnlyContain(t => t.Stays > 0 && t.TotalDays >= 0);
        program.StuckDays.Should().Be(30);
        program.Stuck.All(x => x.Days > 30).Should().BeTrue("only parcels over the limit are listed (none can be yet: every parcel here is new)");
    }

    [PgFact]
    public async Task AT_X_62_The_project_page_lists_every_parcel_with_its_step_owner_money_and_flags()
    {
        var s = await MakeScene();
        var pm = (await _api.As("ProjectManager")).Http;
        var detail = await Ok<ProjectDashboardDto>(await pm.GetAsync($"/api/v1/dashboard/projects/{s.ProjectId}"));

        detail.Project.Id.Should().Be(s.ProjectId);
        detail.Project.Number.Should().Be(s.ProjectNumber);
        detail.Parcels.Should().HaveCount(3);
        detail.Parcels.Select(p => p.Number).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);

        var a = detail.Parcels.Single(p => p.Id == s.A.Id);
        (a.StepKey, a.StepLabel, a.Team, a.Closed).Should().Be(("appraisal", "Appraisal & just compensation", "Appraisal", true));
        (a.AppraisalTotal, a.ApprovedJustCompensation, a.AgreedAmount).Should().Be((102_500.25m, 90_000m, 95_000m));
        a.ActualClosing.Should().Be(new DateOnly(2026, 5, 2));
        a.AnticipatedClosing.Should().Be(new DateOnly(2026, 5, 1));
        a.Flags.Should().NotContain("Closing past due", "it is closed");

        var b = detail.Parcels.Single(p => p.Id == s.B.Id);
        (b.StepKey, b.Team, b.Closed, b.AppraisalTotal, b.AgreedAmount).Should().Be(("title_survey", "Title & Survey", false, null, null));
        b.DaysInStep.Should().BeInRange(0, 1);
        b.Status.Should().NotBeNullOrEmpty();
        b.StatusColor.Should().StartWith("#");
        detail.Parcels.Single(p => p.Id == s.C.Id).StepKey.Should().Be("setup");

        detail.Stages.Should().HaveCount(7).And.Subject.Should().Contain(x => x.StepKey == "title_survey" && x.ParcelsNow == 1, "only this project's parcels are counted");
        detail.Project.Health.Should().Be("Red");
    }

    [PgFact]
    public async Task AT_X_62_Only_active_parcels_are_counted_and_an_unknown_project_is_not_found()
    {
        var pm = (await _api.As("ProjectManager")).Http;
        (await pm.GetAsync($"/api/v1/dashboard/projects/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var admin = (await _api.As("Admin")).Http;
        var project = await Ok<ProjectDto>(await admin.PostAsJsonAsync("/api/v1/projects", new { name = $"Empty {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local" }, LifecycleSupport.Json), HttpStatusCode.Created);
        var empty = await Ok<ProjectDashboardDto>(await admin.GetAsync($"/api/v1/dashboard/projects/{project.Id}"));
        (empty.Project.Parcels, empty.Project.PercentClosed, empty.Project.Health).Should().Be((0, 0, "None"));
        empty.Parcels.Should().BeEmpty();
    }

    [PgFact]
    public async Task AT_X_61_Only_project_managers_and_administrators_get_the_program_view_and_everyone_else_is_refused()
    {
        var s = await MakeScene();
        foreach (var role in Spec.Roles)
        {
            var user = await _api.As(role);
            var allowed = role is "ProjectManager" or "Admin";
            (await user.Http.GetAsync("/api/v1/dashboard/program")).StatusCode.Should().Be(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, role);
            (await user.Http.GetAsync($"/api/v1/dashboard/projects/{s.ProjectId}")).StatusCode.Should().Be(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, role);
        }
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync("/api/v1/dashboard/program")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync($"/api/v1/dashboard/projects/{s.ProjectId}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [PgFact]
    public async Task AT_X_61_Another_agency_sees_none_of_this_agencys_projects()
    {
        var s = await MakeScene();
        var other = await _api.As("Admin", tenant: Guid.NewGuid());
        var res = await other.Http.GetAsync("/api/v1/dashboard/program");
        if (res.StatusCode == HttpStatusCode.OK)
            (await res.Content.ReadFromJsonAsync<ProgramDashboardDto>(LifecycleSupport.Json))!.Projects.Should().NotContain(p => p.Id == s.ProjectId);
        (await other.Http.GetAsync($"/api/v1/dashboard/projects/{s.ProjectId}")).StatusCode.Should().NotBe(HttpStatusCode.OK);
    }
}
