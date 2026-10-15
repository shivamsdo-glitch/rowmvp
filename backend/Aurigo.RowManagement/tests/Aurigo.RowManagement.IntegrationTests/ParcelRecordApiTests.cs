using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.ParcelRecords;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>A parcel's team, negotiation diary and notes on the real API and Postgres (ROW-019, 020, 021). Matches AT-PM-28..29, AT-NG-17.</summary>
public class ParcelRecordApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly Guid OtherTenant = Guid.Parse("00000000-0000-0000-0000-0000000000a7");
    private static readonly string[] AllRoles = { "ProjectManager", "TitleExaminer", "Appraiser", "Negotiator", "Finance", "Admin" };
    private readonly PgApi _api;
    public ParcelRecordApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> t) => (await t).StatusCode;

    private async Task<ParcelDto> NewParcel(TestUser user)
    {
        var project = await Ok<ProjectDto>(await user.Http.PostAsJsonAsync("/api/v1/projects", new { name = $"Records {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local" }, Json), HttpStatusCode.Created);
        return await Ok<ParcelDto>(await user.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 100m, acquisitionType = "Standard" }, Json), HttpStatusCode.Created);
    }

    private static object Member(string name, string role = "Negotiator") => new { role, name, organization = "Okafor Land Services", isConsultant = true, email = "riley@example.com", phone = "555-123-4567" };
    private static object Entry(string date, string contact = "Alex Marlowe", string summary = "Explained the offer.") =>
        new { occurredOn = date, method = "Call", direction = "Outgoing", contactName = contact, summary, outcome = "Will call back" };
    private static string Today(int addDays = 0) => DateTime.UtcNow.AddDays(addDays).ToString("yyyy-MM-dd");

    private async Task EnsureOtherTenantAsync()
    {
        using var scope = _api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();
        if (!await db.Tenants.AnyAsync(t => t.Id == OtherTenant))
        {
            db.Tenants.Add(new Tenant { Id = OtherTenant, Name = "Other Agency A7", Slug = "other-a7", IsActive = true });
            await db.SaveChangesAsync();
        }
        await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().EnsureDefaultRolesAsync(OtherTenant);
    }

    // ── the jobs ──

    [PgFact]
    public async Task AT_PM_28_A_project_manager_assigns_changes_and_removes_the_team_on_a_parcel()
    {
        var pm = await _api.As("ProjectManager");
        var parcel = await NewParcel(pm);

        var riley = await Ok<TeamMemberDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team", Member("  Riley   Okafor "), Json), HttpStatusCode.Created);
        await Ok<TeamMemberDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team", new { role = "ProjectLead", name = "Dana Whitfield", isConsultant = false }, Json), HttpStatusCode.Created);
        var dup = await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team", Member("RILEY okafor"), Json);
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await dup.Content.ReadAsStringAsync()).Should().Contain("team_member_exists");

        var team = await Ok<List<TeamMemberDto>>(await pm.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/team"));
        team.Select(m => (m.Role.ToString(), m.Name)).Should().Equal(("ProjectLead", "Dana Whitfield"), ("Negotiator", "Riley Okafor"));

        var changed = await Ok<TeamMemberDto>(await pm.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team/{riley.Id}", new { role = "Appraiser", name = "Riley Okafor", isConsultant = false, notes = "Moved over" }, Json));
        changed.Role.ToString().Should().Be("Appraiser");
        (await pm.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/team/{riley.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await pm.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/team/{riley.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Ok<List<TeamMemberDto>>(await pm.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/team"))).Should().ContainSingle();
    }

    [PgFact]
    public async Task AT_NG_17_A_negotiator_keeps_the_diary_newest_first_and_cannot_date_it_in_the_future()
    {
        var pm = await _api.As("ProjectManager");
        var ng = await _api.As("Negotiator");
        var parcel = await NewParcel(pm);

        await Ok<DiaryEntryDto>(await ng.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", Entry(Today(-7), "Alex Marlowe", "Introduced the project."), Json), HttpStatusCode.Created);
        var latest = await Ok<DiaryEntryDto>(await ng.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary",
            new { occurredOn = Today(), method = "Meeting", direction = "Incoming", contactName = "Jordan Reyes", summary = "Site walk.", outcome = "Review in two weeks" }, Json), HttpStatusCode.Created);
        latest.CreatedBy.Should().Be(ng.UserId);

        var future = await ng.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", Entry(Today(3)), Json);
        future.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await future.Content.ReadAsStringAsync()).Should().Contain("cannot be dated in the future");

        var diary = await Ok<List<DiaryEntryDto>>(await pm.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/diary"));
        diary.Select(e => e.ContactName).Should().Equal("Jordan Reyes", "Alex Marlowe");
        diary[0].Outcome.Should().Be("Review in two weeks");
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", Entry(Today()), Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the project manager reads the diary; the negotiator keeps it");

        (await ng.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/diary/{latest.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_type = 'ParcelDiaryEntry' and entity_id = @id and action = 'Delete' and changed_by = @u", ("id", latest.Id), ("u", ng.UserId)))
            .Should().Be(1);
    }

    [PgFact]
    public async Task AT_NG_17_A_party_can_be_the_contact_and_a_missing_one_is_refused()
    {
        var pm = await _api.As("ProjectManager");
        var ng = await _api.As("Negotiator");
        var parcel = await NewParcel(pm);
        var party = await Ok<JsonElement>(await ng.Http.PostAsJsonAsync("/api/v1/parties", new { kind = "Person", name = $"Pat {Guid.NewGuid().ToString("N")[..6]}" }, Json), HttpStatusCode.Created);

        var entry = await Ok<DiaryEntryDto>(await ng.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary",
            new { occurredOn = Today(), method = "Email", direction = "Outgoing", partyId = party.GetProperty("id").GetGuid(), summary = "Sent the offer package." }, Json), HttpStatusCode.Created);

        entry.ContactName.Should().Be(party.GetProperty("name").GetString());
        (await ng.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", new { occurredOn = Today(), method = "Email", direction = "Outgoing", partyId = Guid.NewGuid(), summary = "x" }, Json))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ng.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", new { occurredOn = Today(), method = "Email", direction = "Outgoing", summary = "no contact" }, Json))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [PgFact]
    public async Task AT_PM_29_A_project_manager_writes_and_deletes_notes_and_sees_who_wrote_them()
    {
        var pm = await _api.As("ProjectManager");
        var parcel = await NewParcel(pm);

        var note = await Ok<ParcelNoteDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/notes", new { text = "  Owner prefers email.  " }, Json), HttpStatusCode.Created);
        note.Text.Should().Be("Owner prefers email.");
        note.CreatedBy.Should().Be(pm.UserId);
        var second = await Ok<ParcelNoteDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/notes", new { text = "Second note" }, Json), HttpStatusCode.Created);

        (await Ok<List<ParcelNoteDto>>(await pm.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/notes"))).Select(n => n.Text).Should().Equal("Second note", "Owner prefers email.");
        (await pm.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/notes/{note.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Ok<List<ParcelNoteDto>>(await pm.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/notes"))).Should().ContainSingle().Which.Id.Should().Be(second.Id);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_type = 'ParcelNote' and entity_id = @id and action = 'Delete'", ("id", note.Id))).Should().Be(1);
    }

    // ── every role, both directions ──

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task Each_role_can_do_exactly_what_the_parcels_and_negotiations_pages_allow(string role)
    {
        var admin = await _api.As("Admin");
        var parcel = await NewParcel(admin);
        var seedMember = await Ok<TeamMemberDto>(await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team", Member("Seed Counsel", "Counsel"), Json), HttpStatusCode.Created);
        var seedEntry = await Ok<DiaryEntryDto>(await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", Entry(Today(), "Seed"), Json), HttpStatusCode.Created);
        var seedNote = await Ok<ParcelNoteDto>(await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/notes", new { text = "seed" }, Json), HttpStatusCode.Created);
        var user = await _api.As(role);
        bool Can(string page, string action) => Spec.Can(role, page, action);
        HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;

        (await Status(user.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/team"))).Should().Be(Expect(Can("parcels", "read"), HttpStatusCode.OK));
        (await Status(user.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/notes"))).Should().Be(Expect(Can("parcels", "read"), HttpStatusCode.OK));
        (await Status(user.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/diary"))).Should().Be(Expect(Can("negotiations", "read"), HttpStatusCode.OK));

        (await Status(user.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team", Member($"By {role}", "Other"), Json))).Should().Be(Expect(Can("parcels", "create"), HttpStatusCode.Created));
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team/{seedMember.Id}", new { role = "Counsel", name = "Seed Counsel", isConsultant = true, notes = $"by {role}" }, Json)))
            .Should().Be(Expect(Can("parcels", "update"), HttpStatusCode.OK));
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/notes", new { text = $"by {role}" }, Json))).Should().Be(Expect(Can("parcels", "create"), HttpStatusCode.Created));
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", Entry(Today(), $"By {role}"), Json))).Should().Be(Expect(Can("negotiations", "create"), HttpStatusCode.Created));
        (await Status(user.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/diary/{seedEntry.Id}"))).Should().Be(Expect(Can("negotiations", "delete"), HttpStatusCode.NoContent));
        (await Status(user.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/notes/{seedNote.Id}"))).Should().Be(Expect(Can("parcels", "delete"), HttpStatusCode.NoContent));
        (await Status(user.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/team/{seedMember.Id}"))).Should().Be(Expect(Can("parcels", "delete"), HttpStatusCode.NoContent));
        if (!Can("parcels", "create"))
            (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_notes where text = @t", ("t", $"by {role}"))).Should().Be(0, "a refused request changes nothing");
    }

    public static IEnumerable<object[]> Roles() => AllRoles.Select(r => new object[] { r });

    [PgFact]
    public async Task Without_signing_in_nothing_about_a_parcels_records_is_available()
    {
        var anon = _api.Factory.CreateClient();
        foreach (var part in new[] { "team", "diary", "notes" })
            (await anon.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/{part}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── races, tenancy, schema, input ──

    [PgFact]
    public async Task Ten_people_assigning_the_same_person_to_the_same_role_at_once_produce_exactly_one_assignment()
    {
        var pm = await _api.As("ProjectManager");
        var parcel = await NewParcel(pm);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team", Member(i % 2 == 0 ? "Riley Okafor" : "RILEY  OKAFOR"), Json)));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Where(r => r.StatusCode != HttpStatusCode.Created).Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Conflict);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_team_members where parcel_id = @p", ("p", parcel.Id))).Should().Be(1);
    }

    [PgFact]
    public async Task One_agency_never_sees_or_changes_another_agencys_team_diary_or_notes()
    {
        await EnsureOtherTenantAsync();
        var mine = await _api.As("Admin");
        var theirs = await _api.As("Admin", OtherTenant);
        var parcel = await NewParcel(mine);
        var member = await Ok<TeamMemberDto>(await mine.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team", Member("Secret Person"), Json), HttpStatusCode.Created);
        var note = await Ok<ParcelNoteDto>(await mine.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/notes", new { text = "secret" }, Json), HttpStatusCode.Created);
        var entry = await Ok<DiaryEntryDto>(await mine.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", Entry(Today()), Json), HttpStatusCode.Created);

        foreach (var part in new[] { "team", "diary", "notes" })
        {
            (await theirs.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/{part}")).StatusCode.Should().Be(HttpStatusCode.NotFound, part);
        }
        (await theirs.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team", Member("Intruder"), Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/notes", new { text = "x" }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", Entry(Today()), Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/team/{member.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/notes/{note.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/diary/{entry.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_team_members where name = 'Intruder'")).Should().Be(0);
    }

    [PgFact]
    public async Task A_split_parcel_keeps_its_records_for_reading_but_takes_no_new_ones()
    {
        var pm = await _api.As("ProjectManager");
        var admin = await _api.As("Admin");
        var parcel = await NewParcel(pm);
        await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/notes", new { text = "before the split" }, Json);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/split", new { parts = new[] { new { areaSqFt = 40m } } }, Json)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await Ok<List<ParcelNoteDto>>(await pm.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/notes"))).Should().ContainSingle();
        foreach (var res in new[]
        {
            await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/notes", new { text = "late" }, Json),
            await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team", Member("Late"), Json),
            await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", Entry(Today()), Json),
        })
        {
            res.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await res.Content.ReadAsStringAsync()).Should().Contain("parcel_not_active");
        }
    }

    [PgFact]
    public async Task The_record_tables_have_the_keys_and_indexes_the_code_relies_on_and_the_database_refuses_a_second_assignment()
    {
        var pm = await _api.As("ProjectManager");
        var parcel = await NewParcel(pm);
        var indexes = (await PgTestDatabase.QueryAsync(_api.Database, "select indexname from pg_indexes where tablename in ('parcel_team_members', 'parcel_diary_entries', 'parcel_notes')")).Select(r => (string)r[0]!).ToList();
        indexes.Should().Contain(new[] { "ix_parcel_team_members_parcel_id_role_name_key", "ix_parcel_diary_entries_parcel_id_occurred_on", "ix_parcel_notes_parcel_id" });

        const string insert = "insert into parcel_team_members (id, tenant_id, parcel_id, role, name, name_key, is_consultant, created_at, created_by, updated_at, updated_by) " +
                              "values (gen_random_uuid(), @t, @p, 'Counsel', 'X', 'X', false, now(), @t, now(), @t)";
        await PgTestDatabase.QueryAsync(_api.Database, insert, ("t", pm.TenantId), ("p", parcel.Id));
        var again = () => PgTestDatabase.QueryAsync(_api.Database, insert, ("t", pm.TenantId), ("p", parcel.Id));
        (await again.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23505");
    }

    [PgTheory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"text\":123}")]
    [InlineData("{\"text\":\"   \"}")]
    public async Task A_malformed_note_body_is_a_400_never_a_500(string body)
    {
        var pm = await _api.As("ProjectManager");
        var parcel = await NewParcel(pm);
        var res = await pm.PostJson($"/api/v1/parcels/{parcel.Id}/notes", body);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest, await res.Content.ReadAsStringAsync());
    }

    [PgFact]
    public async Task Hostile_and_oversized_input_is_stored_as_text_or_refused_cleanly()
    {
        var pm = await _api.As("ProjectManager");
        var ng = await _api.As("Negotiator");
        var parcel = await NewParcel(pm);
        var script = "<script>alert('x')</script>";

        (await Ok<ParcelNoteDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/notes", new { text = script }, Json), HttpStatusCode.Created)).Text.Should().Be(script);
        (await Ok<TeamMemberDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team", Member("Robert'); DROP TABLE parcels;--"), Json), HttpStatusCode.Created)).Name.Should().Contain("DROP TABLE");
        (await pm.PostJson($"/api/v1/parcels/{parcel.Id}/notes", JsonSerializer.Serialize(new { text = "null\0char" }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.PostJson($"/api/v1/parcels/{parcel.Id}/notes", JsonSerializer.Serialize(new { text = new string('x', 4001) }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team", Member(new string('x', 201)), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team", new { role = "Wizard", name = "X" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/team", new { role = "Other", name = "X", email = "nope" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ng.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", new { occurredOn = "not a date", method = "Call", direction = "Outgoing", contactName = "A", summary = "x" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ng.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", new { occurredOn = "1900-01-01", method = "Call", direction = "Outgoing", contactName = "A", summary = "x" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ng.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", new { occurredOn = Today(), method = "Telepathy", direction = "Outgoing", contactName = "A", summary = "x" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ng.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/diary", new { occurredOn = Today(), method = "Call", direction = "Outgoing", contactName = "A", summary = new string('x', 2001) }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/team")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await pm.Http.GetAsync("/api/v1/parcels/not-a-guid/notes")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PgFact]
    public async Task The_demo_agency_opens_with_a_team_a_diary_and_a_note_on_the_offer_parcel()
    {
        var pm = await _api.As("ProjectManager");
        var bridge = (await Ok<PagedResult<ProjectSummaryDto>>(await pm.Http.GetAsync("/api/v1/projects?search=DEMO-002"))).Items.Single();
        var parcels = await Ok<PagedResult<ParcelSummaryDto>>(await pm.Http.GetAsync($"/api/v1/parcels?projectId={bridge.Id}"));
        var id = parcels.Items.Single(p => p.Number == "001").Id;

        (await Ok<List<TeamMemberDto>>(await pm.Http.GetAsync($"/api/v1/parcels/{id}/team"))).Select(m => m.Name).Should().Contain(new[] { "Dana Whitfield", "Riley Okafor", "Allison Jackson" });
        (await Ok<List<DiaryEntryDto>>(await pm.Http.GetAsync($"/api/v1/parcels/{id}/diary"))).Should().HaveCount(2);
        (await Ok<List<ParcelNoteDto>>(await pm.Http.GetAsync($"/api/v1/parcels/{id}/notes"))).Should().Contain(n => n.Text.Contains("email"));
    }
}
