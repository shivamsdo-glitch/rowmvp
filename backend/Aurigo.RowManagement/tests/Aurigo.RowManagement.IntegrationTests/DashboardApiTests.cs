using System.Net;
using System.Net.Http.Json;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Dashboard;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>Phase 1.5 (ADR 0022): each person's dashboard (what they did, what is waiting) and the manager roll-up, on the real API and real Postgres. Matches AT-X-58, AT-X-59.</summary>
public class DashboardApiTests : IClassFixture<PgApi>
{
    private const string DemoPassword = "qwertyuiop";
    private readonly PgApi _api;
    public DashboardApiTests(PgApi api) => _api = api;

    private static Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK) => LifecycleSupport.Ok<T>(r, expected);

    /// <summary>A real seeded person (an AppUser row), so the manager's team list can show them.</summary>
    private async Task<(HttpClient Http, Guid Id)> SignIn(string username)
    {
        var res = await _api.Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username, password = DemoPassword });
        var body = (await res.Content.ReadFromJsonAsync<AuthResponse>(RowApiFactory.JsonOptions))!;
        return (_api.Factory.ClientWithToken(body.Token), body.User.Id);
    }

    private static async Task<MyDashboardDto> Me(HttpClient http) => await Ok<MyDashboardDto>(await http.GetAsync("/api/v1/dashboard/me"));

    private static int Pending(MyDashboardDto d, string key) => d.Pending.Single(p => p.Key == key).Count;

    private static async Task<ParcelDto> NewParcel(HttpClient pm)
    {
        var project = await Ok<ProjectDto>(await pm.PostAsJsonAsync("/api/v1/projects", new { name = $"Dash {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local" }, LifecycleSupport.Json), HttpStatusCode.Created);
        return await Ok<ParcelDto>(await pm.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 100m, acquisitionType = "Standard" }, LifecycleSupport.Json), HttpStatusCode.Created);
    }

    [PgFact]
    public async Task AT_X_58_My_dashboard_counts_what_I_did_and_not_what_somebody_else_did()
    {
        var (pm, _) = await SignIn("rowprojectmanager");
        var (negotiator, negotiatorId) = await SignIn("rownegotiator");
        var (surveyor, _) = await SignIn("surveyor");
        var before = await Me(negotiator);
        var surveyorBefore = await Me(surveyor);

        // The project manager makes a project and a parcel; the surveyor records an easement; the negotiator logs a call.
        var parcel = await NewParcel(pm);
        (await surveyor.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/easements", new { number = $"E-{Guid.NewGuid().ToString("N")[..6]}" }, LifecycleSupport.Json)).StatusCode.Should().Be(HttpStatusCode.Created);
        var log = await negotiator.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/contact-log", new { contactedAt = "2026-10-01T10:00:00Z", mode = "Phone", notes = "First call" }, LifecycleSupport.Json);
        log.StatusCode.Should().Be(HttpStatusCode.Created, await log.Content.ReadAsStringAsync());

        var after = await Me(negotiator);
        after.UserId.Should().Be(negotiatorId);
        after.Done.Created.Should().BeGreaterThan(before.Done.Created, "the negotiator created a contact-log entry");
        after.Done.ByKind.Should().Contain(k => k.Kind.StartsWith("Parcel contact log"));
        after.Done.ByKind.Should().NotContain(k => k.Kind == "Project" || k.Kind == "Parcel easement", "those were other people's work");
        after.Done.Days.Should().HaveCount(7);
        after.Done.Days[^1].Count.Should().BeGreaterThan(0, "it happened today");
        after.Done.Recent.Should().NotBeEmpty();
        after.Done.LastActiveAt.Should().NotBeNull();

        var surveyorAfter = await Me(surveyor);
        surveyorAfter.Done.ByKind.Should().Contain(k => k.Kind == "Parcel easement" && k.Created >= 1);
        surveyorAfter.Done.Created.Should().BeGreaterThan(surveyorBefore.Done.Created);
        surveyorAfter.Done.ByKind.Should().NotContain(k => k.Kind.StartsWith("Parcel contact log") || k.Kind == "Project", "that was somebody else's work");

        var pmAfter = await Me(pm);
        pmAfter.Done.ByKind.Should().Contain(k => k.Kind == "Project" && k.Created >= 1);
    }

    [PgFact]
    public async Task AT_X_58_Pending_work_is_only_what_my_role_can_open_and_a_request_waits_for_somebody_else()
    {
        var (pm, _) = await SignIn("rowprojectmanager");
        var (finance, _) = await SignIn("rowfinance");
        var (surveyor, _) = await SignIn("surveyor");

        var pmBefore = await Me(pm);
        var financeBefore = await Me(finance);
        var request = await pm.PostAsJsonAsync("/api/v1/acquisition-requests", new { requesterType = "Internal", requesterName = "Design", property = "Lot 9 Harbor Rd", estimatedAmount = 1000m }, LifecycleSupport.Json);
        request.StatusCode.Should().Be(HttpStatusCode.Created, await request.Content.ReadAsStringAsync());

        var pmAfter = await Me(pm);
        Pending(pmAfter, "my-requests").Should().Be(Pending(pmBefore, "my-requests") + 1, "it is the manager's own request, still waiting");
        var financeAfter = await Me(finance);
        Pending(financeAfter, "requests-to-decide").Should().Be(Pending(financeBefore, "requests-to-decide") + 1, "finance can decide it");
        pmAfter.Pending.Should().NotContain(p => p.Key == "requests-to-decide" && p.Count > Pending(pmBefore, "requests-to-decide"), "nobody decides their own request");

        // A role with no access to the requests page is not shown request work at all.
        var surveyorView = await Me(surveyor);
        surveyorView.Pending.Should().NotContain(p => p.Key == "requests-to-decide" || p.Key == "my-requests");
        surveyorView.Pending.Should().OnlyContain(p => p.Link.StartsWith("/"));
        financeAfter.Roles.Should().Equal("Finance");
    }

    [PgFact]
    public async Task AT_X_59_The_team_roll_up_is_for_managers_and_lists_each_person_with_their_own_numbers()
    {
        var (pm, pmId) = await SignIn("rowprojectmanager");
        var (admin, _) = await SignIn("admin");
        var (surveyor, surveyorId) = await SignIn("surveyor");
        var (negotiator, _) = await SignIn("rownegotiator");

        // Some work from two different people.
        var parcel = await NewParcel(pm);
        (await surveyor.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/easements", new { number = $"E-{Guid.NewGuid().ToString("N")[..6]}" }, LifecycleSupport.Json)).StatusCode.Should().Be(HttpStatusCode.Created);

        foreach (var manager in new[] { pm, admin })
        {
            var team = await Ok<TeamDashboardDto>(await manager.GetAsync("/api/v1/dashboard/team"));
            team.Tiles.Select(t => t.Key).Should().Contain(new[] { "projects", "parcels", "awaiting-decision", "deadlines-overdue", "roe-expiring", "closings-due" });
            team.Tiles.Single(t => t.Key == "projects").Value.Should().BeGreaterThan(0);
            team.Days.Should().HaveCount(7);
            team.ByKind.Should().Contain(k => k.Kind == "Project");
            var mine = team.Members.Should().ContainSingle(m => m.UserId == surveyorId).Subject;
            mine.Created.Should().BeGreaterThan(0);
            mine.Roles.Should().Equal("Surveyor");
            mine.LastActiveAt.Should().NotBeNull();
            team.Members.Should().OnlyHaveUniqueItems(m => m.UserId);
            team.Members.Select(m => m.Total).Should().BeInDescendingOrder();
        }

        var teamView = await Ok<TeamDashboardDto>(await pm.GetAsync("/api/v1/dashboard/team"));
        teamView.Members.Single(m => m.UserId == pmId).Roles.Should().Equal("ProjectManager");

        // Everyone else is refused, and the page says so for the person's own dashboard.
        (await negotiator.GetAsync("/api/v1/dashboard/team")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await surveyor.GetAsync("/api/v1/dashboard/team")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Me(pm)).IsManager.Should().BeTrue();
        (await Me(negotiator)).IsManager.Should().BeFalse();
    }

    [PgFact]
    public async Task AT_X_58_Every_default_role_gets_a_dashboard_and_only_managers_a_team_view()
    {
        foreach (var role in Spec.Roles)
        {
            var user = await _api.As(role);
            var mine = await user.Http.GetAsync("/api/v1/dashboard/me");
            mine.StatusCode.Should().Be(HttpStatusCode.OK, $"{role} reads the dashboard page: {await mine.Content.ReadAsStringAsync()}");
            var team = await user.Http.GetAsync("/api/v1/dashboard/team");
            team.StatusCode.Should().Be(role is "ProjectManager" or "Admin" ? HttpStatusCode.OK : HttpStatusCode.Forbidden, role);
        }
    }

    [PgFact]
    public async Task AT_X_58_The_dashboard_needs_a_signed_in_person()
    {
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync("/api/v1/dashboard/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync("/api/v1/dashboard/team")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [PgFact]
    public async Task AT_X_58_Another_agency_never_sees_this_agencys_work()
    {
        var admin = await _api.As("Admin");
        var other = await _api.As("Admin", tenant: Guid.NewGuid());
        // The other agency has no tenant row, so it is refused or sees nothing; either way none of this agency's numbers leak.
        var res = await other.Http.GetAsync("/api/v1/dashboard/team");
        if (res.StatusCode == HttpStatusCode.OK)
        {
            var team = (await res.Content.ReadFromJsonAsync<TeamDashboardDto>(LifecycleSupport.Json))!;
            team.Tiles.Single(t => t.Key == "projects").Value.Should().Be(0);
            team.Members.Should().BeEmpty();
        }
        (await admin.Http.GetAsync("/api/v1/dashboard/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
