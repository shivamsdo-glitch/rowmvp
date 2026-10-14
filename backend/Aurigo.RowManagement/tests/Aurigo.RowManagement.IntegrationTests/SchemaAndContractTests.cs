using System.Text.Json;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>The real database, as migrations build it, and the published contracts other layers rely on.</summary>
public class SchemaAndContractTests : IClassFixture<PgApi>
{
    private readonly PgApi _api;
    public SchemaAndContractTests(PgApi api) => _api = api;

    private static readonly string[] CoreTables =
    {
        "tenants", "app_users", "roles", "user_roles", "user_tokens", "audit_log",
        "role_page_permissions", "documents", "saved_filter_profiles",
        "projects", "project_cost_estimates", "project_budgets", "row_authorizations", "project_number_settings", "project_number_counters",
    };

    [PgFact]
    public async Task Migrations_apply_cleanly_to_an_empty_database_and_create_the_core_tables()
    {
        var tables = (await PgTestDatabase.QueryAsync(_api.Database,
            "select table_name from information_schema.tables where table_schema='public'")).Select(r => (string)r[0]!).ToList();

        tables.Should().Contain(CoreTables);
        tables.Should().Contain("__ef_migrations_history");
    }

    [PgFact]
    public async Task Every_migration_in_the_assembly_is_recorded_as_applied()
    {
        using var scope = _api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();
        var applied = await db.Database.GetAppliedMigrationsAsync();

        pending.Should().BeEmpty();
        applied.Should().NotBeEmpty();
    }

    /// <summary>
    /// If someone changes an entity or its configuration and forgets `dotnet ef migrations add`, production
    /// would run with a schema that does not match the code. This fails the build instead.
    /// </summary>
    [PgFact]
    public void The_migrations_match_the_current_model_so_no_migration_was_forgotten()
    {
        using var scope = _api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();

        var migrationsAssembly = db.GetService<IMigrationsAssembly>();
        var snapshotModel = migrationsAssembly.ModelSnapshot?.Model;
        if (snapshotModel is IMutableModel mutable) snapshotModel = mutable.FinalizeModel();
        snapshotModel = db.GetService<IModelRuntimeInitializer>().Initialize(snapshotModel!);

        var differences = db.GetService<IMigrationsModelDiffer>().GetDifferences(
            snapshotModel.GetRelationalModel(),
            db.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        differences.Should().BeEmpty(
            "the model changed without a migration. Run: dotnet ef migrations add <Name> (see CLAUDE.md). Differences: "
            + string.Join("; ", differences.Select(d => d.GetType().Name)));
    }

    [PgFact]
    public async Task Every_tenant_scoped_table_has_a_non_null_tenant_id()
    {
        var rows = await PgTestDatabase.QueryAsync(_api.Database, """
            select t.table_name,
                   (select c.is_nullable from information_schema.columns c
                     where c.table_schema='public' and c.table_name=t.table_name and c.column_name='tenant_id') as nullable
            from information_schema.tables t
            where t.table_schema='public' and t.table_type='BASE TABLE'
              and t.table_name not in ('tenants','__ef_migrations_history')
            """);

        var problems = rows.Where(r => r[1] is null || (string)r[1]! != "NO").Select(r => (string)r[0]!).ToList();
        problems.Should().BeEmpty("every table except tenants must carry a NOT NULL tenant_id; offenders: " + string.Join(", ", problems));
    }

    [PgFact]
    public async Task Primary_keys_are_uuids_and_timestamps_are_timezone_aware()
    {
        var badKeys = await PgTestDatabase.QueryAsync(_api.Database, """
            select c.table_name from information_schema.columns c
            where c.table_schema='public' and c.column_name='id' and c.data_type <> 'uuid'
              and c.table_name <> '__ef_migrations_history'
            """);
        var badTimes = await PgTestDatabase.QueryAsync(_api.Database, """
            select c.table_name || '.' || c.column_name from information_schema.columns c
            where c.table_schema='public' and c.column_name in ('created_at','updated_at','deleted_at','occurred_at')
              and c.data_type <> 'timestamp with time zone'
            """);

        badKeys.Should().BeEmpty();
        badTimes.Should().BeEmpty("naive timestamps lose their zone and shift between servers");
    }

    [PgFact]
    public async Task Uniqueness_the_application_relies_on_is_enforced_by_the_database_itself()
    {
        var indexes = (await PgTestDatabase.QueryAsync(_api.Database,
            "select indexdef from pg_indexes where schemaname='public'")).Select(r => (string)r[0]!).ToList();

        indexes.Should().Contain(i => i.Contains("UNIQUE") && i.Contains("role_page_permissions") && i.Contains("role_id") && i.Contains("page_key"));
        indexes.Should().Contain(i => i.Contains("UNIQUE") && i.Contains("saved_filter_profiles") && i.Contains("user_id") && i.Contains("page_key") && i.Contains("name"));
        indexes.Should().Contain(i => i.Contains("documents") && i.Contains("entity_type") && i.Contains("entity_id"));
    }

    [PgFact]
    public async Task Saved_filters_are_stored_as_real_jsonb()
    {
        var type = (await PgTestDatabase.QueryAsync(_api.Database,
            "select data_type from information_schema.columns where table_name='saved_filter_profiles' and column_name='filter_json'"))[0][0];

        type.Should().Be("jsonb");
    }

    [PgFact]
    public async Task The_demo_tenant_is_seeded_with_every_role_and_every_page_permission()
    {
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from tenants")).Should().Be(1);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from roles")).Should().Be(Spec.Roles.Length);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from role_page_permissions"))
            .Should().Be(Spec.Roles.Length * Spec.Pages.Length);
    }

    // ---- The permission contract -------------------------------------------------------------

    [PgTheory]
    [MemberData(nameof(Spec.RoleAndPage), MemberType = typeof(Spec))]
    public async Task The_permissions_endpoint_gives_every_role_exactly_the_access_in_the_spec(string role, string page)
    {
        var user = await _api.As(role);

        var all = await user.Permissions();
        var row = all.EnumerateArray().Single(p => p.GetProperty("pageKey").GetString() == page);

        row.GetProperty("canCreate").GetBoolean().Should().Be(Spec.Can(role, page, "create"), $"{role} create on {page}");
        row.GetProperty("canRead").GetBoolean().Should().Be(Spec.Can(role, page, "read"), $"{role} read on {page}");
        row.GetProperty("canUpdate").GetBoolean().Should().Be(Spec.Can(role, page, "update"), $"{role} update on {page}");
        row.GetProperty("canDelete").GetBoolean().Should().Be(Spec.Can(role, page, "delete"), $"{role} delete on {page}");
    }

    [PgTheory]
    [MemberData(nameof(Spec.EachRole), MemberType = typeof(Spec))]
    public async Task The_permissions_endpoint_lists_every_page_exactly_once(string role)
    {
        var user = await _api.As(role);

        var keys = (await user.Permissions()).EnumerateArray().Select(p => p.GetProperty("pageKey").GetString()).ToList();

        keys.Should().BeEquivalentTo(Spec.Pages);
        keys.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// vault/acceptance/permission-matrix.json is what the frontend tests read, so the UI's idea of who
    /// sees what cannot drift from the API's. Regenerate deliberately with ROW_UPDATE_CONTRACT=1.
    /// </summary>
    [Fact]
    public void The_published_permission_matrix_contract_matches_the_spec()
    {
        var expected = new
        {
            note = "Generated from the backend spec (IntegrationTests/Support/Spec.cs). B=read, S=create/read/update, P=full, null=no access.",
            roles = Spec.Roles,
            pages = Spec.Pages,
            matrix = Spec.Roles.ToDictionary(r => r, r => Spec.Pages.ToDictionary(p => p, p => Spec.Level(r, p) == '-' ? null : Spec.Level(r, p).ToString())),
            attachmentPages = Spec.AttachmentPage,
        };
        var json = JsonSerializer.Serialize(expected, new JsonSerializerOptions { WriteIndented = true });
        var path = Path.Combine(RepoRoot(), "vault", "acceptance", "permission-matrix.json");

        if (Environment.GetEnvironmentVariable("ROW_UPDATE_CONTRACT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json.ReplaceLineEndings("\n") + "\n");
        }

        File.Exists(path).Should().BeTrue($"{path} is missing; run the tests once with ROW_UPDATE_CONTRACT=1");
        Compact(File.ReadAllText(path)).Should().Be(Compact(json),
                "the contract file is stale; rerun with ROW_UPDATE_CONTRACT=1 and commit it");
    }

    private static string Compact(string json) => JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found above the test binaries.");
    }
}
