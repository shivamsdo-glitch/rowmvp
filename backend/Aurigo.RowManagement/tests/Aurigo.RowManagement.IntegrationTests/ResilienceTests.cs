using System.Net;
using System.Net.Http.Json;
using System.Text;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>What happens under load, on restart, with two agencies, and when the database misbehaves. Real Postgres throughout.</summary>
public class ResilienceTests : IClassFixture<PgApi>
{
    private static readonly Guid DevTenant = new("00000000-0000-0000-0000-000000000001");
    private readonly PgApi _api;
    public ResilienceTests(PgApi api) => _api = api;

    // ---- concurrency --------------------------------------------------------------------------

    [PgFact]
    public async Task Twenty_people_saving_the_same_view_name_at_once_get_one_success_and_nineteen_clean_conflicts()
    {
        var user = await _api.As("ProjectManager");
        var body = "{\"pageKey\":\"parcels\",\"name\":\"Race\",\"filterJson\":\"{}\"}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => user.PostJson("/api/v1/saved-filters", body)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(19);
        responses.Should().OnlyContain(r => (int)r.StatusCode < 500);
        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from saved_filter_profiles where user_id=@u and name='Race'", ("u", user.UserId))).Should().Be(1);
    }

    [PgFact]
    public async Task Twenty_five_simultaneous_uploads_to_one_record_all_succeed_and_all_appear()
    {
        var admin = await _api.As("Admin");
        var entity = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 25)
            .Select(i => admin.Upload("Parcel", entity, $"file-{i}.pdf", Encoding.UTF8.GetBytes($"content {i}"))));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var list = await admin.ReadJson(await admin.Http.GetAsync($"/api/v1/documents?entityType=Parcel&entityId={entity}"));
        list.GetArrayLength().Should().Be(25);
        list.EnumerateArray().Select(d => d.GetProperty("fileName").GetString()).Should().OnlyHaveUniqueItems();
    }

    [PgFact]
    public async Task Ten_simultaneous_deletes_of_one_document_never_error_and_leave_it_deleted()
    {
        var admin = await _api.As("Admin");
        var id = (await admin.ReadJson(await admin.Upload("Parcel", Guid.NewGuid(), "x.pdf", new byte[] { 1 }))).GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => admin.Http.DeleteAsync($"/api/v1/documents/{id}")));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.NoContent || r.StatusCode == HttpStatusCode.NotFound || r.StatusCode == HttpStatusCode.Conflict);
        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().BeGreaterOrEqualTo(1);
        (await admin.Http.GetAsync($"/api/v1/documents/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PgFact]
    public async Task Renaming_two_views_to_the_same_name_at_once_leaves_exactly_one_with_that_name()
    {
        var user = await _api.As("ProjectManager");
        async Task<Guid> Make(string n) => (await user.ReadJson(await user.PostJson("/api/v1/saved-filters",
            $"{{\"pageKey\":\"projects\",\"name\":\"{n}\",\"filterJson\":\"{{}}\"}}"))).GetProperty("id").GetGuid();
        var a = await Make("first"); var b = await Make("second");

        var results = await Task.WhenAll(new[] { a, b }.Select(id =>
            user.Http.PutAsJsonAsync($"/api/v1/saved-filters/{id}", new { name = "Same", filterJson = "{}" })));

        results.Should().OnlyContain(r => (int)r.StatusCode < 500);
        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from saved_filter_profiles where user_id=@u and name='Same'", ("u", user.UserId))).Should().Be(1);
    }

    [PgFact]
    public async Task Two_hundred_concurrent_permission_reads_all_succeed_with_identical_answers()
    {
        var user = await _api.As("Negotiator");

        var bodies = await Task.WhenAll(Enumerable.Range(0, 200).Select(async _ =>
        {
            var r = await user.Http.GetAsync("/api/v1/auth/permissions");
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            return await r.Content.ReadAsStringAsync();
        }));

        bodies.Distinct().Should().HaveCount(1);
    }

    [PgFact]
    public async Task A_burst_of_mixed_writes_from_every_role_at_once_produces_no_server_errors()
    {
        var calls = new List<Task<HttpResponseMessage>>();
        foreach (var role in Spec.Roles)
        {
            var user = await _api.As(role);
            for (var i = 0; i < 8; i++)
            {
                var entity = Guid.NewGuid();
                calls.Add(user.Upload("Parcel", entity, $"{role}-{i}.pdf", new byte[] { 1, 2, 3 }));
                calls.Add(user.PostJson("/api/v1/saved-filters", $"{{\"pageKey\":\"dashboard\",\"name\":\"{role}-{i}\",\"filterJson\":\"{{}}\"}}"));
            }
        }

        var responses = await Task.WhenAll(calls);

        responses.Should().OnlyContain(r => (int)r.StatusCode < 500);
    }

    // ---- two agencies --------------------------------------------------------------------------

    private async Task<Guid> NewTenant(string name)
    {
        var id = Guid.NewGuid();
        using var scope = _api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();
        db.Tenants.Add(new Tenant { Id = id, Name = name, Slug = "t-" + id.ToString("N")[..8], IsActive = true });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().EnsureDefaultRolesAsync(id);
        return id;
    }

    [PgFact]
    public async Task A_second_agency_sees_none_of_the_first_agencys_documents_or_views_even_as_Admin_with_the_same_user_id()
    {
        var other = await NewTenant("Other DOT");
        var sharedUser = Guid.NewGuid();
        var a = await _api.As("Admin", user: sharedUser);
        var b = await _api.As("Admin", tenant: other, user: sharedUser);
        var entity = Guid.NewGuid();
        var doc = (await a.ReadJson(await a.Upload("Parcel", entity, "confidential.pdf", Encoding.UTF8.GetBytes("A only")))).GetProperty("id").GetGuid();
        await a.PostJson("/api/v1/saved-filters", "{\"pageKey\":\"parcels\",\"name\":\"A view\",\"filterJson\":\"{}\"}");

        (await b.ReadJson(await b.Http.GetAsync($"/api/v1/documents?entityType=Parcel&entityId={entity}"))).GetArrayLength().Should().Be(0);
        (await b.Http.GetAsync($"/api/v1/documents/{doc}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.Http.DeleteAsync($"/api/v1/documents/{doc}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await b.ReadJson(await b.Http.GetAsync("/api/v1/saved-filters?pageKey=parcels"))).GetArrayLength().Should().Be(0);

        // ...and A's data is untouched by B's attempts.
        (await a.Http.GetAsync($"/api/v1/documents/{doc}/content")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [PgFact]
    public async Task Every_row_written_for_an_agency_is_stamped_with_that_agency_and_nothing_leaks_across()
    {
        var other = await NewTenant("Stamp DOT");
        var b = await _api.As("Admin", tenant: other);
        var docId = (await b.ReadJson(await b.Upload("Parcel", Guid.NewGuid(), "b.pdf", new byte[] { 1 }))).GetProperty("id").GetGuid();

        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from documents where id=@i and tenant_id=@t", ("i", docId), ("t", other))).Should().Be(1);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_id=@i and tenant_id=@t", ("i", docId), ("t", other))).Should().BeGreaterThan(0);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_id=@i and tenant_id<>@t", ("i", docId), ("t", other))).Should().Be(0);
    }

    [PgFact]
    public async Task An_agencys_edited_permissions_apply_immediately_to_that_agency_only()
    {
        var other = await NewTenant("Custom DOT");
        var entity = Guid.NewGuid();
        var examinerA = await _api.As("TitleResearcher");
        var examinerB = await _api.As("TitleResearcher", tenant: other);
        (await examinerB.Upload("TitleRecord", entity, "a.pdf", new byte[] { 1 })).StatusCode.Should().Be(HttpStatusCode.Created);

        // That agency's administrator removes the right to attach title documents.
        await PgTestDatabase.QueryAsync(_api.Database, """
            update role_page_permissions set can_update=false, template='Custom'
            where tenant_id=@t and page_key='title'
              and role_id=(select id from roles where tenant_id=@t and name='TitleResearcher')
            """, ("t", other));

        // No new sign-in needed: the very next request reflects it, and the other agency is unaffected.
        (await examinerB.Upload("TitleRecord", entity, "b.pdf", new byte[] { 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await examinerA.Upload("TitleRecord", entity, "c.pdf", new byte[] { 1 })).StatusCode.Should().Be(HttpStatusCode.Created);
        var perms = await examinerB.Permissions();
        perms.EnumerateArray().Single(p => p.GetProperty("pageKey").GetString() == "title").GetProperty("canUpdate").GetBoolean().Should().BeFalse();
    }

    // ---- restart safety ------------------------------------------------------------------------

    [PgFact]
    public async Task Restarting_the_app_changes_no_data_keeps_admin_edits_and_back_fills_a_missing_page()
    {
        await using var first = RowApiFactory.WithPostgres();
        _ = first.Services;
        var db = first.PgDatabase!;
        var before = new[]
        {
            await PgTestDatabase.ScalarAsync(db, "select count(*) from tenants"),
            await PgTestDatabase.ScalarAsync(db, "select count(*) from roles"),
            await PgTestDatabase.ScalarAsync(db, "select count(*) from app_users"),
            await PgTestDatabase.ScalarAsync(db, "select count(*) from role_page_permissions"),
        };

        // An administrator edits one permission; and one page row is missing (as if a new page shipped).
        await PgTestDatabase.QueryAsync(db, "update role_page_permissions set can_update=false, template='Custom' where page_key='title' and role_id=(select id from roles where name='TitleResearcher')");
        await PgTestDatabase.QueryAsync(db, "delete from role_page_permissions where page_key='payments' and role_id=(select id from roles where name='Finance')");

        await using var second = RowApiFactory.WithPostgres(existingDatabase: db);
        _ = second.Services; // boots again: migrate (nothing to do) + seed (add-only)

        var after = new[]
        {
            await PgTestDatabase.ScalarAsync(db, "select count(*) from tenants"),
            await PgTestDatabase.ScalarAsync(db, "select count(*) from roles"),
            await PgTestDatabase.ScalarAsync(db, "select count(*) from app_users"),
            await PgTestDatabase.ScalarAsync(db, "select count(*) from role_page_permissions"),
        };
        after.Should().Equal(before, "a restart must not duplicate or lose anything");
        (await PgTestDatabase.ScalarAsync(db, "select count(*) from role_page_permissions p join roles r on r.id=p.role_id where r.name='TitleResearcher' and p.page_key='title' and p.can_update=false and p.template='Custom'"))
            .Should().Be(1, "the administrator's edit survives a restart");
        (await PgTestDatabase.ScalarAsync(db, "select count(*) from role_page_permissions p join roles r on r.id=p.role_id where r.name='Finance' and p.page_key='payments' and p.can_delete=true"))
            .Should().Be(1, "the missing page is added back with its default");

        var examiner = await TestUser.SignInAsync(second, "TitleResearcher", null, Guid.NewGuid());
        (await examiner.Upload("TitleRecord", Guid.NewGuid(), "x.pdf", new byte[] { 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [PgFact]
    public async Task Production_boots_on_a_clean_database_with_no_demo_tenant_users_or_dev_token()
    {
        await using var production = RowApiFactory.WithPostgres(new() { ["Jwt:SigningKey"] = new string('p', 48), ["Security:TaxIdKey"] = Support.TestKeys.TaxIdKey }, environment: "Production");
        _ = production.Services;
        var db = production.PgDatabase!;

        (await PgTestDatabase.ScalarAsync(db, "select count(*) from tenants")).Should().Be(0);
        (await PgTestDatabase.ScalarAsync(db, "select count(*) from app_users")).Should().Be(0);
        (await production.CreateClient().GetAsync("/api/v1/auth/dev-token?role=Admin")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await production.CreateClient().GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- database trouble ---------------------------------------------------------------------

    private const string DeadDatabase = "Host=127.0.0.1;Port=1;Username=x;Password=y;Database=z;Timeout=2;Command Timeout=2";

    [PgFact]
    public async Task Health_reports_503_when_the_database_cannot_be_reached_and_the_app_stays_up()
    {
        await using var broken = RowApiFactory.WithPostgres(new()
        {
            ["ConnectionStrings:Postgres"] = DeadDatabase,
            ["Database:MigrateOnStartup"] = "false",
            ["Database:SeedOnStartup"] = "false",
        }, existingDatabase: "unused");

        var health = await broken.CreateClient().GetAsync("/health");

        health.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await health.Content.ReadAsStringAsync()).Should().Be("Unhealthy");
        // The process is still serving requests: an unauthenticated call is still a clean 401.
        (await broken.CreateClient().GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [PgFact]
    public void The_app_refuses_to_start_when_it_must_migrate_and_the_database_is_unreachable()
    {
        using var broken = RowApiFactory.WithPostgres(new() { ["ConnectionStrings:Postgres"] = DeadDatabase }, existingDatabase: "unused");

        var act = () => broken.CreateClient();

        act.Should().Throw<Exception>("failing fast beats serving a half-working app");
    }

    // ---- audit --------------------------------------------------------------------------------

    [PgFact]
    public async Task Every_upload_and_removal_is_in_the_audit_trail_with_who_did_it_and_never_the_file_contents()
    {
        var examiner = await _api.As("TitleResearcher");
        var marker = "TOP-SECRET-CONTENT-" + Guid.NewGuid();
        var dto = await examiner.ReadJson(await examiner.Upload("TitleRecord", Guid.NewGuid(), "deed.pdf", Encoding.UTF8.GetBytes(marker)));
        var id = dto.GetProperty("id").GetGuid();
        await examiner.Http.DeleteAsync($"/api/v1/documents/{id}");

        var rows = await PgTestDatabase.QueryAsync(_api.Database,
            "select action::text, entity_type, field, new_value, changed_by, tenant_id from audit_log where entity_id=@i order by changed_at", ("i", id));

        rows.Should().Contain(r => (string)r[1]! == "Document" && ((string)r[0]! == "Create" || (string)r[0]! == "0"));
        rows.Should().Contain(r => (string?)r[2] == "IsDeleted" && (string?)r[3] == "True");
        rows.Should().OnlyContain(r => (Guid)r[4]! == examiner.UserId && (Guid)r[5]! == DevTenant);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where new_value like @m or old_value like @m", ("m", "%" + marker + "%")))
            .Should().Be(0, "file bytes must never be copied into the audit log");
    }

    [PgFact]
    public async Task Renaming_a_saved_view_records_the_old_and_new_name()
    {
        var user = await _api.As("ProjectManager");
        var id = (await user.ReadJson(await user.PostJson("/api/v1/saved-filters",
            "{\"pageKey\":\"parcels\",\"name\":\"Before\",\"filterJson\":\"{}\"}"))).GetProperty("id").GetGuid();
        await user.Http.PutAsJsonAsync($"/api/v1/saved-filters/{id}", new { name = "After", filterJson = "{}" });

        var rows = await PgTestDatabase.QueryAsync(_api.Database,
            "select old_value, new_value from audit_log where entity_id=@i and field='Name'", ("i", id));

        rows.Should().ContainSingle().Which.Should().Equal("Before", "After");
    }

    // ---- accounts on the real database --------------------------------------------------------

    [PgFact]
    public async Task A_seeded_user_can_be_invited_activate_sign_in_and_reset_their_password_and_no_secret_reaches_the_audit_log()
    {
        const string email = "negotiator@row.local";
        var client = _api.Factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { username = email, password = "whatever-1" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email })).EnsureSuccessStatusCode();
        var activation = _api.Factory.Emails.LastTokenFor(email);
        (await client.PostAsJsonAsync("/api/v1/auth/activate", new { token = activation, password = "Real-Password-1" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/auth/activate", new { token = activation, password = "Real-Password-2" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = email, password = "Real-Password-1" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("token").GetString()!;
        var signedIn = _api.Factory.ClientWithToken(token);
        (await signedIn.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        // A negotiator's own sign-in carries a negotiator's access, not more.
        (await signedIn.GetAsync("/api/v1/admin/ping")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await signedIn.PostAsync("/api/v1/documents", TestUser.Form("Offer", Guid.NewGuid(), "r.pdf", new byte[] { 1 }))).StatusCode.Should().Be(HttpStatusCode.Created);

        (await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email })).EnsureSuccessStatusCode();
        var reset = _api.Factory.Emails.LastTokenFor(email);
        (await client.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = reset, password = "Another-Password-3" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { username = email, password = "Real-Password-1" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { username = email, password = "Another-Password-3" })).StatusCode.Should().Be(HttpStatusCode.OK);

        // The stored hash and the emailed tokens must not appear anywhere in the audit trail.
        var hash = (string)(await PgTestDatabase.QueryAsync(_api.Database, "select password_hash from app_users where email=@e", ("e", email)))[0][0]!;
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where new_value like @h or old_value like @h", ("h", "%" + hash[..20] + "%"))).Should().Be(0);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where new_value like @t or old_value like @t", ("t", "%" + activation + "%"))).Should().Be(0);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from user_tokens where token_hash=@t", ("t", activation))).Should().Be(0, "only a hash of a token is stored, never the token");
    }
}
