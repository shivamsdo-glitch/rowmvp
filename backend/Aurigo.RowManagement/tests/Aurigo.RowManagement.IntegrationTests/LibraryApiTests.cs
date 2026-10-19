using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurigo.RowManagement.Application.Library;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>The Library on the real API and real Postgres: defaults, the administrator's job, every role, races, tenancy, schema and hostile input.</summary>
public class LibraryApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly Guid OtherTenant = Guid.Parse("00000000-0000-0000-0000-0000000000f6");
    private static readonly string[] AllRoles = Aurigo.RowManagement.IntegrationTests.Support.Spec.Roles;

    private readonly PgApi _api;
    public LibraryApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> t) => (await t).StatusCode;

    private static async Task<IReadOnlyList<LibraryFieldSummaryDto>> Fields(TestUser u) =>
        await Ok<List<LibraryFieldSummaryDto>>(await u.Http.GetAsync("/api/v1/library/fields"));

    private static async Task<LibraryFieldDto> Field(TestUser u, string key)
    {
        var summary = (await Fields(u)).Single(f => f.Key == key);
        return await Ok<LibraryFieldDto>(await u.Http.GetAsync($"/api/v1/library/fields/{summary.Id}"));
    }

    private async Task EnsureOtherTenantAsync()
    {
        using var scope = _api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();
        if (!await db.Tenants.AnyAsync(t => t.Id == OtherTenant))
        {
            db.Tenants.Add(new Tenant { Id = OtherTenant, Name = "Other Agency F6", Slug = "other-f6", IsActive = true });
            await db.SaveChangesAsync();
        }
        await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().EnsureDefaultRolesAsync(OtherTenant);
    }

    // ── defaults and the administrator's job ──

    [PgFact]
    public async Task AT_AD_28_The_demo_agency_opens_with_the_default_library_and_the_fifteen_parcel_statuses()
    {
        var admin = await _api.As("Admin");

        var fields = await Fields(admin);
        fields.Select(f => f.Key).Should().StartWith(new[] { "parcel_status", "interest_type", "possession_type", "agent_type", "contact_type", "cad_number", "segment", "priority", "owner_type", "relocation_type", "encumbrance_type" });
        fields.Select(f => f.Key).Should().Contain(new[] { "appraisal_type", "mode_of_contact", "easement_type", "offer_type", "demo_status", "disposition_type", "conveyance_type", "fund_type", "title_company" });
        (await Field(admin, "owner_type")).Values.Select(v => v.Value).Should().Equal("CAD Owner", "Commercial Sign Owner", "Fee Owner", "Prior Owner", "Vested Owner");
        (await Field(admin, "relocation_type")).Values.Select(v => v.Value).Should().Equal("Residential", "Non-residential", "Personal Property only");
        (await Field(admin, "encumbrance_type")).Values.Select(v => v.Value).Should().Contain(new[] { "Mortgage Lien", "Mechanic Lien", "Financing Statements (UCC-1)", "Deed of Trust" }).And.HaveCount(17);
        (await Field(admin, "agent_type")).Values.Select(v => v.Value).Should().Contain(new[] { "Row Designer", "Surveyor", "Title Researcher", "Appraiser", "Negotiator", "Broker" });
        (await Field(admin, "appraisal_type")).Values.Should().BeEmpty("the agency fills these in");
        fields.Single(f => f.Key == "possession_type").IsLibraryDriven.Should().BeTrue();
        fields.Single(f => f.Key == "cad_number").IsLibraryDriven.Should().BeFalse();
        (await Field(admin, "possession_type")).Values.Select(v => v.Value).Should().StartWith(new[] { "PUA", "Closing", "Notice of Deposit", "Judgement" });
        (await Field(admin, "interest_type")).Values.Select(v => v.Value).Should().Contain(new[] { "Partial Fee", "Full Fee", "Drainage Easement" });
        var status = await Field(admin, "parcel_status");
        status.Values.Select(v => v.Value).Should().StartWith(new[] { "Inactive", "In Design", "Pre-Acquisition" }).And.Contain(new[] { "Disposition Complete", "Pending Condemnation" });
        status.Values.Single(v => v.IsDefault).Value.Should().Be("Inactive");
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(distinct color) from parcel_statuses where tenant_id = @t", ("t", admin.TenantId))).Should().Be(1, "no colours");
    }

    [PgFact]
    public async Task AT_AD_28_An_administrator_adds_edits_switches_off_and_imports_values_and_defines_a_new_field()
    {
        var admin = await _api.As("Admin");
        var possession = await Field(admin, "possession_type");
        var tag = Guid.NewGuid().ToString("N")[..6];

        var added = await Ok<LibraryFieldDto>(await admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{possession.Id}/values", new { value = $"Deed {tag}" }, Json), HttpStatusCode.Created);
        var deed = added.Values.Single(v => v.Value == $"Deed {tag}");
        var renamed = await Ok<LibraryFieldDto>(await admin.Http.PutAsJsonAsync($"/api/v1/library/fields/{possession.Id}/values/{deed.Id}", new { value = $"Deed of trust {tag}", isActive = false }, Json));
        renamed.Values.Single(v => v.Id == deed.Id).Should().Match<LibraryValueDto>(v => v.Value == $"Deed of trust {tag}" && !v.IsActive);

        var import = await Ok<LibraryImportResultDto>(await admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{possession.Id}/import",
            new { values = new[] { "pua", $"Lease {tag}", $"LEASE {tag}", $"Option {tag}" } }, Json));
        import.Added.Should().Be(2);
        import.AlreadyThere.Should().Be(2);

        var field = await Ok<LibraryFieldDto>(await admin.Http.PostAsJsonAsync("/api/v1/library/fields", new { label = $"Zone {tag}", fieldType = "List" }, Json), HttpStatusCode.Created);
        field.Key.Should().Be($"zone_{tag}");
        field.IsSystem.Should().BeFalse();
        var asText = await Ok<LibraryFieldDto>(await admin.Http.PutAsJsonAsync($"/api/v1/library/fields/{field.Id}", new { label = $"Zoning {tag}", fieldType = "Text" }, Json));
        asText.FieldType.ToString().Should().Be("Text");
        (await admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{field.Id}/values", new { value = "R1" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a text field has no values");
    }

    [PgFact]
    public async Task AT_AD_28_Library_changes_to_the_parcel_status_list_are_the_parcel_statuses_and_a_new_parcel_starts_Inactive()
    {
        var admin = await _api.As("Admin");
        var pm = await _api.As("ProjectManager");
        var status = await Field(admin, "parcel_status");
        var name = $"Staked {Guid.NewGuid().ToString("N")[..6]}";

        await Ok<LibraryFieldDto>(await admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{status.Id}/values", new { value = name }, Json), HttpStatusCode.Created);

        var statuses = await Ok<List<ParcelStatusDto>>(await pm.Http.GetAsync("/api/v1/parcel-rules/statuses"));
        statuses.Single(s => s.Name == name).Should().Match<ParcelStatusDto>(s => s.IsActive && !s.IsInitial);
        (await admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{status.Id}/values", new { value = name.ToLowerInvariant() }, Json)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var inactive = status.Values.Single(v => v.IsDefault);
        var refused = await admin.Http.PutAsJsonAsync($"/api/v1/library/fields/{status.Id}/values/{inactive.Id}", new { value = "Inactive", isActive = false }, Json);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("initial status must be active");

        var project = await Ok<JsonElement>(await pm.Http.PostAsJsonAsync("/api/v1/projects", new { name = $"Library project {name}", oversightType = "Local" }, Json), HttpStatusCode.Created);
        var parcel = await Ok<ParcelDto>(await pm.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.GetProperty("id").GetGuid(), areaSqFt = 1m, acquisitionType = "Standard" }, Json), HttpStatusCode.Created);
        parcel.StatusName.Should().Be("Inactive");
    }

    // ── every role, both directions ──

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task Everyone_can_read_the_library_and_only_the_administrator_can_change_it(string role)
    {
        var admin = await _api.As("Admin");
        var possession = await Field(admin, "possession_type");
        var user = await _api.As(role);
        var allowed = role == "Admin";
        HttpStatusCode Expect(HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;
        var tag = $"{role}-{Guid.NewGuid().ToString("N")[..5]}";

        // The approved matrix gives every role Read on the Library (forms need its dropdown values); only Admin changes it.
        (await Status(user.Http.GetAsync("/api/v1/library/fields"))).Should().Be(HttpStatusCode.OK);
        (await Status(user.Http.GetAsync($"/api/v1/library/fields/{possession.Id}"))).Should().Be(HttpStatusCode.OK);
        (await Status(user.Http.PostAsJsonAsync("/api/v1/library/fields", new { label = $"Field {tag}", fieldType = "Text" }, Json))).Should().Be(Expect(HttpStatusCode.Created));
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/library/fields/{possession.Id}", new { label = "Possession type", fieldType = "List" }, Json))).Should().Be(Expect(HttpStatusCode.OK));
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/library/fields/{possession.Id}/values", new { value = $"Value {tag}" }, Json))).Should().Be(Expect(HttpStatusCode.Created));
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/library/fields/{possession.Id}/values/{possession.Values[0].Id}", new { value = possession.Values[0].Value, isActive = true }, Json))).Should().Be(Expect(HttpStatusCode.OK));
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/library/fields/{possession.Id}/import", new { values = new[] { $"Imp {tag}" } }, Json))).Should().Be(Expect(HttpStatusCode.OK));
        if (!allowed)
            (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from library_values where value like @p", ("p", $"%{tag}%"))).Should().Be(0, "a refused request changes nothing");
    }

    public static IEnumerable<object[]> Roles() => AllRoles.Select(r => new object[] { r });

    [PgFact]
    public async Task Without_signing_in_the_library_is_closed()
    {
        var anon = _api.Factory.CreateClient();
        (await anon.GetAsync("/api/v1/library/fields")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync($"/api/v1/library/fields/{Guid.NewGuid()}/values", new { value = "x" }, Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── races and the database's own rules ──

    [PgFact]
    public async Task Ten_people_adding_the_same_value_at_once_produce_exactly_one_value()
    {
        var admin = await _api.As("Admin");
        var field = await Ok<LibraryFieldDto>(await admin.Http.PostAsJsonAsync("/api/v1/library/fields", new { label = $"Race {Guid.NewGuid().ToString("N")[..6]}", fieldType = "List" }, Json), HttpStatusCode.Created);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{field.Id}/values", new { value = i % 2 == 0 ? "Same" : "SAME" }, Json)));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Where(r => r.StatusCode != HttpStatusCode.Created).Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Conflict);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from library_values where library_field_id = @f", ("f", field.Id))).Should().Be(1);
    }

    [PgFact]
    public async Task Overlapping_imports_never_duplicate_a_value_and_never_fail_with_a_server_error()
    {
        var admin = await _api.As("Admin");
        var field = await Ok<LibraryFieldDto>(await admin.Http.PostAsJsonAsync("/api/v1/library/fields", new { label = $"Imports {Guid.NewGuid().ToString("N")[..6]}", fieldType = "List" }, Json), HttpStatusCode.Created);
        var values = Enumerable.Range(0, 30).Select(i => $"Value {i}").ToArray();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{field.Id}/import", new { values }, Json)));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
        results.Should().Contain(r => r.StatusCode == HttpStatusCode.OK);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from library_values where library_field_id = @f", ("f", field.Id))).Should().Be(30);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(distinct normalized_value) from library_values where library_field_id = @f", ("f", field.Id))).Should().Be(30);
    }

    [PgFact]
    public async Task The_database_itself_refuses_a_second_copy_of_a_value_or_a_field_key()
    {
        var admin = await _api.As("Admin");
        var possession = await Field(admin, "possession_type");
        var dup = () => PgTestDatabase.QueryAsync(_api.Database,
            "insert into library_values (id, tenant_id, library_field_id, value, normalized_value, is_active, sort_order, created_at, created_by, updated_at, updated_by) " +
            "values (gen_random_uuid(), @t, @f, 'pua', 'PUA', true, 99, now(), @t, now(), @t)", ("t", admin.TenantId), ("f", possession.Id));
        (await dup.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23505");
        var key = () => PgTestDatabase.QueryAsync(_api.Database,
            "insert into library_fields (id, tenant_id, key, label, field_type, is_system, sort_order, created_at, created_by, updated_at, updated_by) " +
            "values (gen_random_uuid(), @t, 'possession_type', 'Again', 'List', false, 99, now(), @t, now(), @t)", ("t", admin.TenantId));
        (await key.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23505");
    }

    // ── tenancy ──

    [PgFact]
    public async Task One_agency_never_sees_or_changes_another_agencys_library()
    {
        await EnsureOtherTenantAsync();
        var mine = await _api.As("Admin");
        var theirs = await _api.As("Admin", OtherTenant);
        var possession = await Field(mine, "possession_type");
        var theirPossession = await Field(theirs, "possession_type");
        theirPossession.Id.Should().NotBe(possession.Id);

        await mine.Http.PostAsJsonAsync($"/api/v1/library/fields/{possession.Id}/values", new { value = $"Mine only {Guid.NewGuid().ToString("N")[..6]}" }, Json);
        (await Field(theirs, "possession_type")).Values.Should().HaveCount(4);
        foreach (var url in new[] { $"/api/v1/library/fields/{possession.Id}" })
            (await theirs.Http.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PostAsJsonAsync($"/api/v1/library/fields/{possession.Id}/values", new { value = "Intruder" }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PutAsJsonAsync($"/api/v1/library/fields/{possession.Id}/values/{possession.Values[0].Id}", new { value = "Hijack", isActive = false }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PostAsJsonAsync($"/api/v1/library/fields/{possession.Id}/import", new { values = new[] { "Intruder" } }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PutAsJsonAsync($"/api/v1/library/fields/{possession.Id}", new { label = "Hijacked", fieldType = "List" }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from library_values where value in ('Intruder', 'Hijack')")).Should().Be(0);
    }

    // ── hostile input, audit and schema ──

    [PgTheory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"value\":123}")]
    [InlineData("{\"value\":\"\"}")]
    [InlineData("{\"value\":\"   \"}")]
    public async Task A_malformed_value_body_is_a_400_never_a_500(string body)
    {
        var admin = await _api.As("Admin");
        var field = await Field(admin, "possession_type");
        var res = await admin.PostJson($"/api/v1/library/fields/{field.Id}/values", body);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest, await res.Content.ReadAsStringAsync());
    }

    [PgFact]
    public async Task Awkward_values_are_stored_as_text_or_refused_cleanly()
    {
        var admin = await _api.As("Admin");
        var field = await Field(admin, "agent_type");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var script = $"<script>alert('{tag}')</script>";
        var sql = $"x'); DROP TABLE library_values;-- {tag}";

        var a = await Ok<LibraryFieldDto>(await admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{field.Id}/values", new { value = script }, Json), HttpStatusCode.Created);
        var b = await Ok<LibraryFieldDto>(await admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{field.Id}/values", new { value = sql }, Json), HttpStatusCode.Created);
        a.Values.Select(v => v.Value).Should().Contain(script);
        b.Values.Select(v => v.Value).Should().Contain(sql);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from library_values")).Should().BeGreaterThan(10);

        (await admin.PostJson($"/api/v1/library/fields/{field.Id}/values", JsonSerializer.Serialize(new { value = "null\0char" }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostJson($"/api/v1/library/fields/{field.Id}/values", JsonSerializer.Serialize(new { value = new string('x', 101) }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{field.Id}/import", new { values = Array.Empty<string>() }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{field.Id}/import", new { values = Enumerable.Repeat("v", 501) }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{field.Id}/import", new { values = new[] { "ok", "" } }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Http.PostAsJsonAsync("/api/v1/library/fields", new { label = "", fieldType = "Text" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Http.PostAsJsonAsync("/api/v1/library/fields", new { label = "Odd", fieldType = "Spreadsheet" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Http.GetAsync("/api/v1/library/fields/not-a-guid")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.Http.GetAsync($"/api/v1/library/fields/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.Http.PutAsJsonAsync($"/api/v1/library/fields/{field.Id}/values/{Guid.NewGuid()}", new { value = "x", isActive = true }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PgFact]
    public async Task Library_changes_are_in_the_audit_trail_with_who_made_them()
    {
        var admin = await _api.As("Admin");
        var field = await Field(admin, "contact_type");
        var value = $"Audited {Guid.NewGuid().ToString("N")[..6]}";
        var added = await Ok<LibraryFieldDto>(await admin.Http.PostAsJsonAsync($"/api/v1/library/fields/{field.Id}/values", new { value }, Json), HttpStatusCode.Created);
        var id = added.Values.Single(v => v.Value == value).Id;
        await admin.Http.PutAsJsonAsync($"/api/v1/library/fields/{field.Id}/values/{id}", new { value, isActive = false }, Json);

        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_type = 'LibraryValue' and entity_id = @id and changed_by = @u", ("id", id), ("u", admin.UserId))).Should().BeGreaterThanOrEqualTo(2);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_type = 'LibraryValue' and entity_id = @id and field = 'IsActive'", ("id", id))).Should().Be(1);
    }

    [PgFact]
    public async Task The_library_tables_have_the_keys_and_indexes_the_code_relies_on()
    {
        var indexes = (await PgTestDatabase.QueryAsync(_api.Database, "select indexname from pg_indexes where tablename in ('library_fields', 'library_values')")).Select(r => (string)r[0]!).ToList();
        indexes.Should().Contain(new[] { "ix_library_fields_tenant_id_key", "ix_library_values_library_field_id_normalized_value" });
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from pg_constraint where conrelid = 'library_values'::regclass and contype = 'f'")).Should().Be(1);
    }
}
