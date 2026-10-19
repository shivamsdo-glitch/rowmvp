using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aurigo.RowManagement.Application.Library;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>Phase 1.5 slice 3: the Library's Agents list on the real API and real Postgres (ADR 0015).</summary>
public class LibraryAgentsApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private readonly PgApi _api;
    public LibraryAgentsApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<string> Title(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString()!;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private static Task<HttpResponseMessage> Create(TestUser u, string name, string type = "Surveyor", string? company = "Acme Survey", string? phone = "555-0100", string? email = "a@example.test") =>
        u.Http.PostAsJsonAsync("/api/v1/library/agents", new { name, agentType = type, company, phone, email });

    [PgTheory]
    [MemberData(nameof(EachRole))]
    public async Task AT_AD_38_Everyone_reads_the_agents_list_and_only_the_administrator_changes_it(string role)
    {
        var admin = await _api.As("Admin");
        var agent = await Ok<LibraryAgentDto>(await Create(admin, $"Reader Test {Tag()}"), HttpStatusCode.Created);
        var user = await _api.As(role);
        var writes = role == "Admin";

        (await user.Http.GetAsync("/api/v1/library/agents")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Create(user, $"Writer {role} {Tag()}")).StatusCode.Should().Be(writes ? HttpStatusCode.Created : HttpStatusCode.Forbidden);
        (await user.Http.PutAsJsonAsync($"/api/v1/library/agents/{agent.Id}", new { name = agent.Name, agentType = agent.AgentType, company = agent.Company, phone = "1", email = agent.Email, isActive = true }))
            .StatusCode.Should().Be(writes ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
    }

    public static IEnumerable<object[]> EachRole() => Spec.EachRole();

    [PgFact]
    public async Task AT_AD_38_Signed_out_callers_get_401_on_the_agents_list()
    {
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync("/api/v1/library/agents")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync("/api/v1/library/agents", new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [PgFact]
    public async Task AT_AD_38_An_administrator_adds_an_agent_who_then_appears_filtered_by_job_and_by_active()
    {
        var admin = await _api.As("Admin");
        var name = $"Casey Surveyor {Tag()}";
        var agent = await Ok<LibraryAgentDto>(await Create(admin, name, "surveyor", "  Acme Survey  ", " 555-0101 ", "CASEY@Example.Test"), HttpStatusCode.Created);

        agent.AgentType.Should().Be("Surveyor", "the spelling of the Agent type list wins");
        agent.Company.Should().Be("Acme Survey");
        agent.Email.Should().Be("casey@example.test");
        agent.IsActive.Should().BeTrue();

        var surveyors = await Ok<List<LibraryAgentDto>>(await admin.Http.GetAsync("/api/v1/library/agents?agentType=SURVEYOR&activeOnly=true"));
        surveyors.Should().Contain(a => a.Id == agent.Id).And.OnlyContain(a => a.AgentType == "Surveyor" && a.IsActive);
        (await Ok<List<LibraryAgentDto>>(await admin.Http.GetAsync("/api/v1/library/agents?agentType=Negotiator"))).Should().NotContain(a => a.Id == agent.Id);

        var off = await Ok<LibraryAgentDto>(await admin.Http.PutAsJsonAsync($"/api/v1/library/agents/{agent.Id}",
            new { name, agentType = "Surveyor", company = "Acme Survey", phone = agent.Phone, email = agent.Email, isActive = false }));
        off.IsActive.Should().BeFalse();
        (await Ok<List<LibraryAgentDto>>(await admin.Http.GetAsync("/api/v1/library/agents?activeOnly=true"))).Should().NotContain(a => a.Id == agent.Id);
        (await Ok<List<LibraryAgentDto>>(await admin.Http.GetAsync("/api/v1/library/agents"))).Should().Contain(a => a.Id == agent.Id);
    }

    [PgFact]
    public async Task AT_AD_38_The_same_person_for_the_same_job_and_company_is_entered_once_but_may_hold_other_jobs()
    {
        var admin = await _api.As("Admin");
        var name = $"Dana Both {Tag()}";
        await Ok<LibraryAgentDto>(await Create(admin, name, "Negotiator", "Stateside ROW"), HttpStatusCode.Created);

        var dup = await Create(admin, name.ToUpperInvariant(), "negotiator", " stateside row ");
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Title(dup)).Should().Be("library_agent_exists");
        await Ok<LibraryAgentDto>(await Create(admin, name, "Appraiser", "Stateside ROW"), HttpStatusCode.Created);
        await Ok<LibraryAgentDto>(await Create(admin, name, "Negotiator", "Another Firm"), HttpStatusCode.Created);
    }

    [PgFact]
    public async Task AT_AD_38_The_job_must_be_an_active_value_of_the_Agent_type_list_and_bad_input_is_a_clean_400()
    {
        var admin = await _api.As("Admin");
        var unknown = await Create(admin, $"Nobody {Tag()}", "Wizard");
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(unknown)).Should().Be("unknown_agent_type");

        foreach (var bad in new object[]
        {
            new { name = "", agentType = "Surveyor", company = (string?)null, phone = (string?)null, email = (string?)null },
            new { name = "X", agentType = "", company = (string?)null, phone = (string?)null, email = (string?)null },
            new { name = "X", agentType = "Surveyor", company = (string?)null, phone = (string?)null, email = "not-an-email" },
            new { name = new string('n', 151), agentType = "Surveyor", company = (string?)null, phone = (string?)null, email = (string?)null },
        })
            (await admin.Http.PostAsJsonAsync("/api/v1/library/agents", bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest, JsonSerializer.Serialize(bad));
        (await admin.Http.PutAsJsonAsync($"/api/v1/library/agents/{Guid.NewGuid()}", new { name = "X", agentType = "Surveyor" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PgFact]
    public async Task AT_AD_38_A_job_switched_off_in_the_Agent_type_list_cannot_be_given_to_new_agents_but_old_agents_keep_it()
    {
        var admin = await _api.As("Admin");
        var typeName = $"Temp Job {Tag()}";
        var list = (await Ok<List<LibraryFieldSummaryDto>>(await admin.Http.GetAsync("/api/v1/library/fields"))).Single(f => f.Key == "agent_type");
        var withValue = await Ok<LibraryFieldDto>(await admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{list.Id}/values", new { value = typeName }), HttpStatusCode.Created);
        var agent = await Ok<LibraryAgentDto>(await Create(admin, $"Temp Person {Tag()}", typeName), HttpStatusCode.Created);

        var valueId = withValue.Values.Single(v => v.Value == typeName).Id;
        await Ok<LibraryFieldDto>(await admin.Http.PutAsJsonAsync($"/api/v1/library/fields/{list.Id}/values/{valueId}", new { value = typeName, isActive = false }));

        var fresh = await Create(admin, $"Late Person {Tag()}", typeName);
        (await Title(fresh)).Should().Be("agent_type_inactive");
        // An existing agent can still be edited without changing the job.
        await Ok<LibraryAgentDto>(await admin.Http.PutAsJsonAsync($"/api/v1/library/agents/{agent.Id}",
            new { name = agent.Name, agentType = typeName, company = agent.Company, phone = "555-9999", email = agent.Email, isActive = true }));
    }

    [PgFact]
    public async Task AT_AD_38_Twelve_people_adding_the_same_agent_at_once_produce_exactly_one()
    {
        var admin = await _api.As("Admin");
        var name = $"Racer {Tag()}";
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Create(admin, name, "Broker", "Race Co")));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from library_agents where name=@n", ("n", name))).Should().Be(1);
    }

    [PgFact]
    public async Task AT_AD_38_Another_agency_sees_none_of_this_agencys_agents_and_changes_are_audited()
    {
        var other = Guid.Parse("00000000-0000-0000-0000-0000000000f8");
        using (var scope = _api.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Aurigo.RowManagement.Infrastructure.Persistence.RowDbContext>();
            if (!await db.Tenants.AnyAsync(t => t.Id == other))
            {
                db.Tenants.Add(new Aurigo.RowManagement.Domain.Entities.Tenant { Id = other, Name = "Other Agency F8", Slug = "other-f8", IsActive = true });
                await db.SaveChangesAsync();
            }
            await scope.ServiceProvider.GetRequiredService<Aurigo.RowManagement.Infrastructure.Persistence.Seed.TenantProvisioner>().EnsureDefaultRolesAsync(other);
        }
        var mine = await _api.As("Admin");
        var agent = await Ok<LibraryAgentDto>(await Create(mine, $"Private {Tag()}"), HttpStatusCode.Created);

        var theirs = await _api.As("Admin", tenant: other);
        (await Ok<List<LibraryAgentDto>>(await theirs.Http.GetAsync("/api/v1/library/agents"))).Should().NotContain(a => a.Id == agent.Id);
        (await theirs.Http.PutAsJsonAsync($"/api/v1/library/agents/{agent.Id}", new { name = "Hijack", agentType = "Surveyor" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // The same person can be entered in the other agency: uniqueness is per agency.
        (await Create(theirs, agent.Name)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_id=@i", ("i", agent.Id))).Should().BeGreaterThan(0);
    }
}
