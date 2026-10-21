using System.Net;
using System.Net.Http.Json;
using Aurigo.RowManagement.Application.Dashboard;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>ADR 0025: the Program view's thresholds and step-to-team mapping are the agency's to set, on the real API and real Postgres. Matches AT-X-63.</summary>
public class ProgramSettingsApiTests : IClassFixture<PgApi>
{
    private readonly PgApi _api;
    public ProgramSettingsApiTests(PgApi api) => _api = api;

    private static Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK) => LifecycleSupport.Ok<T>(r, expected);

    private static object Body(int stuck = 30, int warnDays = 90, int warnPct = 75, int redMin = 3, int redPct = 25, params (string Step, string Team)[] steps) =>
        new { stuckDays = stuck, clearDateWarningDays = warnDays, clearDateWarningPercentClosed = warnPct, redOverdueMinimumParcels = redMin, redOverduePercent = redPct, steps = steps.Select(s => new { stepKey = s.Step, team = s.Team }).ToArray() };

    private async Task<HttpClient> Admin() => (await _api.As("Admin")).Http;

    /// <summary>One project, clear date in 30 days, one parcel on the appraisal step, nothing closed.</summary>
    private async Task<(Guid ProjectId, Guid ParcelId)> Scene(HttpClient admin)
    {
        var clear = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30).ToString("yyyy-MM-dd");
        var project = await Ok<ProjectDto>(await admin.PostAsJsonAsync("/api/v1/projects", new { name = $"Rules {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local", rightOfWayClearDate = clear }, LifecycleSupport.Json), HttpStatusCode.Created);
        var parcel = await Ok<ParcelDto>(await admin.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 100m, acquisitionType = "Standard" }, LifecycleSupport.Json), HttpStatusCode.Created);
        foreach (var step in new[] { "title_survey", "appraisal" }) (await admin.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = step }, LifecycleSupport.Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        return (project.Id, parcel.Id);
    }

    [PgFact]
    public async Task AT_X_63_A_new_agency_gets_the_defaults_and_every_step_with_its_default_team()
    {
        var admin = await Admin();
        var s = await Ok<ProgramSettingsDto>(await admin.GetAsync("/api/v1/dashboard/settings"));

        s.Rules.Should().Be(new ProgramRulesDto(30, 90, 75, 3, 25));
        s.Defaults.Should().Be(s.Rules);
        s.Steps.Select(x => x.StepKey).Should().ContainInOrder("setup", "title_survey", "appraisal", "offer", "negotiation", "agreement", "closing");
        s.Steps.Should().OnlyContain(x => x.Team == x.DefaultTeam);
        s.Steps.Single(x => x.StepKey == "appraisal").Team.Should().Be("Appraisal");
        s.Steps.Select(x => x.StepKey).Should().Contain(new[] { "early_approval", "donation_review", "conveyance_terms" }, "steps of the other acquisition types are mappable too");
        s.Teams.Should().Contain(new[] { "Project team", "Title & Survey", "Appraisal", "Negotiation", "Closing" });
        s.Steps.First(x => x.StepKey == "title_survey").Label.Should().Be("Title & survey");
    }

    [PgFact]
    public async Task AT_X_63_Saved_thresholds_are_returned_and_change_how_the_program_judges_a_project()
    {
        var admin = await Admin();
        var pm = (await _api.As("ProjectManager")).Http;
        var (projectId, _) = await Scene(admin);

        var before = (await Ok<ProgramDashboardDto>(await pm.GetAsync("/api/v1/dashboard/program"))).Projects.Single(p => p.Id == projectId);
        before.Health.Should().Be("Amber", "30 days to the clear date with nothing closed is inside the default 90-day window");
        before.Reasons.Should().ContainSingle().Which.Should().Contain("30 days away");

        var saved = await Ok<ProgramSettingsDto>(await admin.PutAsJsonAsync("/api/v1/dashboard/settings", Body(stuck: 45, warnDays: 10, warnPct: 60, redMin: 4, redPct: 40), LifecycleSupport.Json));
        saved.Rules.Should().Be(new ProgramRulesDto(45, 10, 60, 4, 40));
        (await Ok<ProgramSettingsDto>(await admin.GetAsync("/api/v1/dashboard/settings"))).Rules.Should().Be(saved.Rules, "it is stored, not just echoed");

        var program = await Ok<ProgramDashboardDto>(await pm.GetAsync("/api/v1/dashboard/program"));
        program.Projects.Single(p => p.Id == projectId).Health.Should().Be("Green", "the warning window is now 10 days");
        (program.StuckDays, program.Rules).Should().Be((45, saved.Rules), "the view says which thresholds it used");
        (await Ok<ProjectDashboardDto>(await pm.GetAsync($"/api/v1/dashboard/projects/{projectId}"))).Rules.StuckDays.Should().Be(45);

        var reset = await Ok<ProgramSettingsDto>(await admin.DeleteAsync("/api/v1/dashboard/settings"));
        reset.Rules.Should().Be(reset.Defaults);
        (await Ok<ProgramDashboardDto>(await pm.GetAsync("/api/v1/dashboard/program"))).Projects.Single(p => p.Id == projectId).Health.Should().Be("Amber");
    }

    [PgFact]
    public async Task AT_X_63_A_step_can_be_given_another_team_and_the_whole_program_follows_and_resetting_goes_back()
    {
        var admin = await Admin();
        var pm = (await _api.As("ProjectManager")).Http;
        var (projectId, parcelId) = await Scene(admin);

        var saved = await Ok<ProgramSettingsDto>(await admin.PutAsJsonAsync("/api/v1/dashboard/settings", Body(steps: new[] { ("appraisal", "  Valuation  "), ("closing", "Closing"), ("offer", "Negotiation") }), LifecycleSupport.Json));
        saved.Steps.Single(x => x.StepKey == "appraisal").Should().Match<StepTeamDto>(x => x.Team == "Valuation" && x.DefaultTeam == "Appraisal", "the name is trimmed and the default is kept for comparison");
        saved.Steps.Single(x => x.StepKey == "closing").Team.Should().Be("Closing");
        saved.Teams.Should().Contain("Valuation");

        var program = await Ok<ProgramDashboardDto>(await pm.GetAsync("/api/v1/dashboard/program"));
        program.Stages.Single(x => x.StepKey == "appraisal").Team.Should().Be("Valuation");
        program.Teams.Select(t => t.Team).Should().Contain("Valuation");
        var detail = await Ok<ProjectDashboardDto>(await pm.GetAsync($"/api/v1/dashboard/projects/{projectId}"));
        detail.Parcels.Single(p => p.Id == parcelId).Team.Should().Be("Valuation");
        detail.Teams.Select(t => t.Team).Should().Contain("Valuation");

        (await Ok<ProgramSettingsDto>(await admin.PutAsJsonAsync("/api/v1/dashboard/settings", Body(), LifecycleSupport.Json))).Steps.Should().OnlyContain(x => x.Team == x.DefaultTeam, "saving with no steps puts every step back to its default");
        (await Ok<ProjectDashboardDto>(await pm.GetAsync($"/api/v1/dashboard/projects/{projectId}"))).Parcels.Single(p => p.Id == parcelId).Team.Should().Be("Appraisal");

        await Ok<ProgramSettingsDto>(await admin.PutAsJsonAsync("/api/v1/dashboard/settings", Body(steps: new[] { ("appraisal", "Valuation") }), LifecycleSupport.Json));
        var back = await Ok<ProgramSettingsDto>(await admin.DeleteAsync("/api/v1/dashboard/settings"));
        back.Steps.Single(x => x.StepKey == "appraisal").Team.Should().Be("Appraisal");
    }

    [PgFact]
    public async Task AT_X_63_Bad_values_are_refused_and_nothing_is_saved()
    {
        var admin = await Admin();
        var bad = new (string Why, object Body)[]
        {
            ("stuck days 0", Body(stuck: 0)), ("stuck days 366", Body(stuck: 366)), ("warning days 0", Body(warnDays: 0)), ("percent closed 101", Body(warnPct: 101)),
            ("percent closed 0", Body(warnPct: 0)), ("min parcels 0", Body(redMin: 0)), ("overdue percent 0", Body(redPct: 0)), ("overdue percent 101", Body(redPct: 101)),
            ("blank team", Body(steps: new[] { ("appraisal", "  ") })), ("team too long", Body(steps: new[] { ("appraisal", new string('x', 51)) })),
            ("duplicate step", Body(steps: new[] { ("appraisal", "A"), ("appraisal", "B") })), ("blank step key", Body(steps: new[] { (" ", "A") })),
        };
        foreach (var (why, body) in bad)
            (await admin.PutAsJsonAsync("/api/v1/dashboard/settings", body, LifecycleSupport.Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest, why);

        var unknown = await admin.PutAsJsonAsync("/api/v1/dashboard/settings", Body(steps: new[] { ("not_a_step", "Somebody") }), LifecycleSupport.Json);
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("unknown_step");

        (await Ok<ProgramSettingsDto>(await admin.GetAsync("/api/v1/dashboard/settings"))).Rules.Should().Be(new ProgramRulesDto(30, 90, 75, 3, 25), "no refused request changed anything");
        (await admin.PutAsJsonAsync("/api/v1/dashboard/settings", Body(stuck: 365, warnDays: 365, warnPct: 100, redMin: 1000, redPct: 100), LifecycleSupport.Json)).StatusCode.Should().Be(HttpStatusCode.OK, "the limits themselves are allowed");
        await admin.DeleteAsync("/api/v1/dashboard/settings");
    }

    [PgFact]
    public async Task AT_X_63_Only_whoever_may_configure_the_system_can_see_or_change_the_settings()
    {
        foreach (var role in Spec.Roles)
        {
            var user = (await _api.As(role)).Http;
            var allowed = Spec.Can(role, "administration", "update");
            (await user.GetAsync("/api/v1/dashboard/settings")).StatusCode.Should().Be(Spec.Can(role, "administration", "read") ? HttpStatusCode.OK : HttpStatusCode.Forbidden, $"{role} GET");
            (await user.PutAsJsonAsync("/api/v1/dashboard/settings", Body(), LifecycleSupport.Json)).StatusCode.Should().Be(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, $"{role} PUT");
            (await user.DeleteAsync("/api/v1/dashboard/settings")).StatusCode.Should().Be(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, $"{role} DELETE");
        }
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync("/api/v1/dashboard/settings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PutAsJsonAsync("/api/v1/dashboard/settings", Body(), LifecycleSupport.Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [PgFact]
    public async Task AT_X_63_One_agencys_settings_never_reach_another_agency()
    {
        var admin = await Admin();
        await Ok<ProgramSettingsDto>(await admin.PutAsJsonAsync("/api/v1/dashboard/settings", Body(stuck: 77, steps: new[] { ("appraisal", "Valuation") }), LifecycleSupport.Json));
        var other = (await _api.As("Admin", tenant: Guid.NewGuid())).Http;
        var res = await other.GetAsync("/api/v1/dashboard/settings");
        if (res.StatusCode == HttpStatusCode.OK)
        {
            var s = (await res.Content.ReadFromJsonAsync<ProgramSettingsDto>(LifecycleSupport.Json))!;
            s.Rules.StuckDays.Should().Be(30);
            s.Steps.Where(x => x.StepKey == "appraisal").Should().OnlyContain(x => x.Team == "Appraisal");
        }
        await admin.DeleteAsync("/api/v1/dashboard/settings");
    }
}
