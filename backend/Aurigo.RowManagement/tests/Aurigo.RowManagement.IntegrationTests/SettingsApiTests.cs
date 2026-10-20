using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aurigo.RowManagement.Application.Settings;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>Phase 1.5 slice 2: Settings > Users & Roles and Permissions on the real API and real Postgres (ADR 0014).</summary>
public class SettingsApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly PgApi _api;
    public SettingsApiTests(PgApi api) => _api = api;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<string> Title(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("title").GetString()!;

    private static string Unique(string stem) => $"{stem}{Guid.NewGuid():N}"[..Math.Min(40, stem.Length + 8)];

    private static async Task<IReadOnlyList<RoleDto>> Roles(TestUser u) => await Ok<List<RoleDto>>(await u.Http.GetAsync("/api/v1/roles"));

    private static async Task<RoleDto> RoleNamed(TestUser u, string name) => (await Roles(u)).Single(r => r.Name == name);

    // ── who may open Settings ──

    [PgTheory]
    [MemberData(nameof(Spec_EachRole))]
    public async Task AT_AD_30_Only_roles_with_Administration_access_can_use_Settings_and_everyone_else_is_refused(string role)
    {
        var user = await _api.As(role);
        var allowed = Spec.Can(role, "administration", "read");

        (await user.Http.GetAsync("/api/v1/roles")).StatusCode.Should().Be(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        (await user.Http.GetAsync("/api/v1/users")).StatusCode.Should().Be(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        (await user.Http.PostAsJsonAsync("/api/v1/roles", new { name = Unique("Nope"), description = "x" }))
            .StatusCode.Should().Be(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden);
    }

    public static IEnumerable<object[]> Spec_EachRole() => Spec.EachRole();

    [PgFact]
    public async Task AT_AD_30_Signed_out_callers_get_401_on_every_settings_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        foreach (var url in new[] { "/api/v1/roles", "/api/v1/users" })
            (await anon.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
        (await anon.PostAsJsonAsync("/api/v1/roles", new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PutAsJsonAsync($"/api/v1/roles/{Guid.NewGuid()}/permissions", new { permissions = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── the roles and the grid ──

    [PgFact]
    public async Task AT_AD_31_The_roles_list_has_the_eleven_default_roles_each_with_every_page_and_the_approved_defaults()
    {
        var admin = await _api.As("Admin");
        var roles = await Roles(admin);

        roles.Select(r => r.Name).Take(11).Should().Equal("ProjectManager", "Designer", "DesignApprover", "Surveyor", "TitleResearcher", "Appraiser",
            "AppraisalApprover", "Negotiator", "RelocationAgent", "Finance", "Admin");
        roles.Take(11).Should().OnlyContain(r => r.IsSystem && r.Permissions.Count == Spec.Pages.Length);
        roles.Single(r => r.Name == "Admin").IsLocked.Should().BeTrue();
        roles.Single(r => r.Name == "Admin").Permissions.Should().OnlyContain(p => p.CanCreate && p.CanRead && p.CanUpdate && p.CanDelete);
        var surveyor = roles.Single(r => r.Name == "Surveyor").Permissions.Single(p => p.PageKey == "survey");
        (surveyor.CanCreate, surveyor.CanRead, surveyor.CanUpdate, surveyor.CanDelete).Should().Be((true, true, true, true));
    }

    [PgFact]
    public async Task AT_AD_32_An_administrator_creates_a_role_that_starts_with_no_access_then_grants_pages_and_it_takes_effect()
    {
        var admin = await _api.As("Admin");
        var name = Unique("Field Reviewer");
        var created = await Ok<RoleDto>(await admin.Http.PostAsJsonAsync("/api/v1/roles", new { name, description = "Walks parcels" }), HttpStatusCode.Created);

        created.IsSystem.Should().BeFalse();
        created.Permissions.Should().HaveCount(Spec.Pages.Length).And.OnlyContain(p => !p.CanRead && !p.CanCreate && !p.CanUpdate && !p.CanDelete);

        var grid = created.Permissions.Select(p => p.PageKey == "parcels" ? p with { CanRead = true, CanUpdate = true } : p.PageKey == "dashboard" ? p with { CanRead = true } : p).ToList();
        var saved = await Ok<RoleDto>(await admin.Http.PutAsJsonAsync($"/api/v1/roles/{created.Id}/permissions", new { permissions = grid }));
        saved.Permissions.Single(p => p.PageKey == "parcels").Should().Be(new RolePermissionDto("parcels", false, true, true, false));

        // A person in the new role gets exactly that access.
        var login = await CreateUserAsync(admin, created.Id);
        var me = await SignInAsync(login.Username, "Passw0rd-abc");
        var perms = (await me.GetFromJsonAsync<List<JsonElement>>("/api/v1/auth/permissions"))!;
        perms.Single(p => p.GetProperty("pageKey").GetString() == "parcels").GetProperty("canUpdate").GetBoolean().Should().BeTrue();
        perms.Single(p => p.GetProperty("pageKey").GetString() == "projects").GetProperty("canRead").GetBoolean().Should().BeFalse();
        (await me.GetAsync("/api/v1/roles")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [PgFact]
    public async Task AT_AD_32_Role_names_are_unique_ignoring_case_and_validated()
    {
        var admin = await _api.As("Admin");
        var name = Unique("Clerk");
        await Ok<RoleDto>(await admin.Http.PostAsJsonAsync("/api/v1/roles", new { name, description = (string?)null }), HttpStatusCode.Created);

        var dup = await admin.Http.PostAsJsonAsync("/api/v1/roles", new { name = name.ToUpperInvariant(), description = (string?)null });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Title(dup)).Should().Be("duplicate_role");
        (await Title(await admin.Http.PostAsJsonAsync("/api/v1/roles", new { name = "admin", description = (string?)null }))).Should().Be("duplicate_role");

        foreach (var bad in new[] { "", "   ", "<script>", new string('x', 61), "-lead" })
            (await admin.Http.PostAsJsonAsync("/api/v1/roles", new { name = bad, description = (string?)null })).StatusCode.Should().Be(HttpStatusCode.BadRequest, bad);
    }

    [PgFact]
    public async Task AT_AD_33_The_Admin_role_is_locked_and_built_in_roles_keep_their_names_but_can_be_re_described_and_re_granted()
    {
        var admin = await _api.As("Admin");
        var adminRole = await RoleNamed(admin, "Admin");
        var grid = adminRole.Permissions.Select(p => p with { CanDelete = false }).ToList();

        (await Title(await admin.Http.PutAsJsonAsync($"/api/v1/roles/{adminRole.Id}/permissions", new { permissions = grid }))).Should().Be("admin_locked");
        (await Title(await admin.Http.PutAsJsonAsync($"/api/v1/roles/{adminRole.Id}", new { name = "Admin", description = "x" }))).Should().Be("admin_locked");
        (await Title(await admin.Http.DeleteAsync($"/api/v1/roles/{adminRole.Id}"))).Should().Be("system_role");

        var finance = await RoleNamed(admin, "Finance");
        (await Title(await admin.Http.PutAsJsonAsync($"/api/v1/roles/{finance.Id}", new { name = "Money", description = finance.Description }))).Should().Be("system_role");
        (await Title(await admin.Http.DeleteAsync($"/api/v1/roles/{finance.Id}"))).Should().Be("system_role");
        var renamedDescription = await Ok<RoleDto>(await admin.Http.PutAsJsonAsync($"/api/v1/roles/{finance.Id}", new { name = "Finance", description = "Pays the bills" }));
        renamedDescription.Description.Should().Be("Pays the bills");
        await admin.Http.PutAsJsonAsync($"/api/v1/roles/{finance.Id}", new { name = "Finance", description = finance.Description });
    }

    [PgFact]
    public async Task AT_AD_33_A_grid_that_gives_create_update_or_delete_without_read_is_refused_and_unknown_pages_are_refused()
    {
        var admin = await _api.As("Admin");
        var role = await Ok<RoleDto>(await admin.Http.PostAsJsonAsync("/api/v1/roles", new { name = Unique("Grid"), description = (string?)null }), HttpStatusCode.Created);

        (await admin.Http.PutAsJsonAsync($"/api/v1/roles/{role.Id}/permissions",
            new { permissions = new[] { new RolePermissionDto("parcels", true, false, false, false) } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Http.PutAsJsonAsync($"/api/v1/roles/{role.Id}/permissions",
            new { permissions = new[] { new RolePermissionDto("nonsense", false, true, false, false) } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Http.PutAsJsonAsync($"/api/v1/roles/{role.Id}/permissions",
            new { permissions = new[] { new RolePermissionDto("parcels", false, true, false, false), new RolePermissionDto("parcels", false, true, false, false) } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.Http.PutAsJsonAsync($"/api/v1/roles/{Guid.NewGuid()}/permissions", new { permissions = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PgFact]
    public async Task AT_AD_34_A_custom_role_can_be_renamed_and_deleted_only_when_nobody_is_in_it()
    {
        var admin = await _api.As("Admin");
        var role = await Ok<RoleDto>(await admin.Http.PostAsJsonAsync("/api/v1/roles", new { name = Unique("Temp"), description = (string?)null }), HttpStatusCode.Created);
        var newName = Unique("Renamed");
        (await Ok<RoleDto>(await admin.Http.PutAsJsonAsync($"/api/v1/roles/{role.Id}", new { name = newName, description = "d" }))).Name.Should().Be(newName);

        var member = await CreateUserAsync(admin, role.Id);
        (await Title(await admin.Http.DeleteAsync($"/api/v1/roles/{role.Id}"))).Should().Be("role_in_use");

        var finance = await RoleNamed(admin, "Finance");
        await Ok<SettingsUserDto>(await admin.Http.PutAsJsonAsync($"/api/v1/users/{member.Id}",
            new { firstName = "A", lastName = "B", email = member.Email, roleId = finance.Id, isActive = true }));
        (await admin.Http.DeleteAsync($"/api/v1/roles/{role.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Roles(admin)).Should().NotContain(r => r.Id == role.Id);
    }

    // ── users ──

    private async Task<SettingsUserDto> CreateUserAsync(TestUser admin, Guid roleId, string? username = null, string password = "Passw0rd-abc")
    {
        username ??= Unique("user").ToLowerInvariant();
        return await Ok<SettingsUserDto>(await admin.Http.PostAsJsonAsync("/api/v1/users",
            new { username, firstName = "Test", lastName = "Person", email = $"{username}@example.test", password, roleId }), HttpStatusCode.Created);
    }

    private async Task<HttpClient> SignInAsync(string username, string password)
    {
        var response = await _api.Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        return _api.Factory.ClientWithToken(token);
    }

    [PgFact]
    public async Task AT_AD_35_An_administrator_creates_a_user_who_signs_in_at_once_with_the_right_role()
    {
        var admin = await _api.As("Admin");
        var negotiator = await RoleNamed(admin, "Negotiator");
        var user = await CreateUserAsync(admin, negotiator.Id, password: "Another-Pass-9");

        user.RoleName.Should().Be("Negotiator");
        user.IsActive.Should().BeTrue();
        var me = await SignInAsync(user.Username.ToUpperInvariant(), "Another-Pass-9");
        (await me.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("roles")[0].GetString().Should().Be("Negotiator");
        (await Users(admin)).Should().Contain(u => u.Id == user.Id);
    }

    private static async Task<IReadOnlyList<SettingsUserDto>> Users(TestUser u) => await Ok<List<SettingsUserDto>>(await u.Http.GetAsync("/api/v1/users"));

    [PgFact]
    public async Task AT_AD_35_Usernames_and_emails_are_unique_across_the_platform_and_input_is_validated()
    {
        var admin = await _api.As("Admin");
        var finance = await RoleNamed(admin, "Finance");
        var user = await CreateUserAsync(admin, finance.Id);

        (await Title(await admin.Http.PostAsJsonAsync("/api/v1/users", new { username = user.Username.ToUpperInvariant(), firstName = "A", lastName = "B", email = "fresh@example.test", password = "Passw0rd-abc", roleId = finance.Id })))
            .Should().Be("duplicate_username");
        (await Title(await admin.Http.PostAsJsonAsync("/api/v1/users", new { username = Unique("other").ToLowerInvariant(), firstName = "A", lastName = "B", email = user.Email, password = "Passw0rd-abc", roleId = finance.Id })))
            .Should().Be("duplicate_email");
        (await Title(await admin.Http.PostAsJsonAsync("/api/v1/users", new { username = "admin", firstName = "A", lastName = "B", email = "x@example.test", password = "Passw0rd-abc", roleId = finance.Id })))
            .Should().Be("duplicate_username");

        foreach (var bad in new object[]
        {
            new { username = "ab", firstName = "A", lastName = "B", email = "a@example.test", password = "Passw0rd-abc", roleId = finance.Id },
            new { username = "has space", firstName = "A", lastName = "B", email = "a@example.test", password = "Passw0rd-abc", roleId = finance.Id },
            new { username = "okname", firstName = "", lastName = "B", email = "a@example.test", password = "Passw0rd-abc", roleId = finance.Id },
            new { username = "okname", firstName = "A", lastName = "B", email = "not-an-email", password = "Passw0rd-abc", roleId = finance.Id },
            new { username = "okname", firstName = "A", lastName = "B", email = "a@example.test", password = "short", roleId = finance.Id },
            new { username = "okname", firstName = "A", lastName = "B", email = "a@example.test", password = "Passw0rd-abc", roleId = Guid.Empty },
        })
            (await admin.Http.PostAsJsonAsync("/api/v1/users", bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest, JsonSerializer.Serialize(bad));
        (await admin.Http.PostAsJsonAsync("/api/v1/users", new { username = "okname2", firstName = "A", lastName = "B", email = "a2@example.test", password = "Passw0rd-abc", roleId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PgFact]
    public async Task AT_AD_36_Changing_a_role_deactivating_and_resetting_a_password_take_effect_at_the_next_sign_in()
    {
        var admin = await _api.As("Admin");
        var finance = await RoleNamed(admin, "Finance");
        var appraiser = await RoleNamed(admin, "Appraiser");
        var user = await CreateUserAsync(admin, finance.Id);

        var moved = await Ok<SettingsUserDto>(await admin.Http.PutAsJsonAsync($"/api/v1/users/{user.Id}",
            new { firstName = "New", lastName = "Name", email = user.Email, roleId = appraiser.Id, isActive = true }));
        moved.RoleName.Should().Be("Appraiser");
        (await Users(admin)).Single(u => u.Id == user.Id).FirstName.Should().Be("New");
        var me = await SignInAsync(user.Username, "Passw0rd-abc");
        (await me.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("roles").GetArrayLength().Should().Be(1);

        (await admin.Http.PostAsJsonAsync($"/api/v1/users/{user.Id}/reset-password", new { password = "Brand-New-Pass-1" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _api.Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username = user.Username, password = "Passw0rd-abc" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await SignInAsync(user.Username, "Brand-New-Pass-1");
        (await admin.Http.PostAsJsonAsync($"/api/v1/users/{user.Id}/reset-password", new { password = "short" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await Ok<SettingsUserDto>(await admin.Http.PutAsJsonAsync($"/api/v1/users/{user.Id}",
            new { firstName = "New", lastName = "Name", email = user.Email, roleId = appraiser.Id, isActive = false }));
        var blocked = await _api.Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username = user.Username, password = "Brand-New-Pass-1" });
        blocked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Title(blocked)).Should().Be("account_unavailable");
    }

    [PgFact]
    public async Task AT_AD_37_You_cannot_deactivate_yourself_and_the_last_active_administrator_cannot_be_demoted()
    {
        // A fresh agency with exactly one administrator, so the shared demo agency's users are never at risk.
        var tenant = Guid.NewGuid();
        Guid adminUserId;
        using (var scope = _api.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Aurigo.RowManagement.Infrastructure.Persistence.RowDbContext>();
            db.Tenants.Add(new Aurigo.RowManagement.Domain.Entities.Tenant { Id = tenant, Name = "Solo Agency", Slug = "solo-" + tenant.ToString("N")[..8], IsActive = true });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<Aurigo.RowManagement.Infrastructure.Persistence.Seed.TenantProvisioner>().EnsureDefaultRolesAsync(tenant);
            var role = db.Roles.IgnoreQueryFilters().Single(r => r.TenantId == tenant && r.Name == "Admin");
            var user = new Aurigo.RowManagement.Domain.Entities.AppUser { TenantId = tenant, FirstName = "Solo", LastName = "Admin", Email = $"solo-{tenant:N}@example.test", Username = $"solo-{tenant:N}", IsActive = true };
            user.UserRoles.Add(new Aurigo.RowManagement.Domain.Entities.UserRole { TenantId = tenant, UserId = user.Id, RoleId = role.Id });
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();
            adminUserId = user.Id;
        }
        var solo = await _api.As("Admin", tenant: tenant, user: adminUserId);
        var roles = await Roles(solo);
        var adminRole = roles.Single(r => r.Name == "Admin");
        var finance = roles.Single(r => r.Name == "Finance");
        var me = (await Users(solo)).Single();

        var deactivate = await solo.Http.PutAsJsonAsync($"/api/v1/users/{me.Id}", new { firstName = "Solo", lastName = "Admin", email = me.Email, roleId = adminRole.Id, isActive = false });
        (await Title(deactivate)).Should().Be("cannot_deactivate_self");
        var demote = await solo.Http.PutAsJsonAsync($"/api/v1/users/{me.Id}", new { firstName = "Solo", lastName = "Admin", email = me.Email, roleId = finance.Id, isActive = true });
        demote.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Title(demote)).Should().Be("last_admin");

        // With a second administrator the first may step down; then the second is the last one and is protected.
        var second = await CreateUserAsync(solo, adminRole.Id);
        await Ok<SettingsUserDto>(await solo.Http.PutAsJsonAsync($"/api/v1/users/{me.Id}", new { firstName = "Solo", lastName = "Admin", email = me.Email, roleId = finance.Id, isActive = true }));
        var lastOne = await solo.Http.PutAsJsonAsync($"/api/v1/users/{second.Id}", new { firstName = "Test", lastName = "Person", email = second.Email, roleId = finance.Id, isActive = false });
        (await Title(lastOne)).Should().Be("last_admin");
    }

    [PgFact]
    public async Task AT_AD_37_Another_agency_sees_none_of_this_agencys_roles_or_users()
    {
        var otherTenant = Guid.Parse("00000000-0000-0000-0000-0000000000f7");
        using (var scope = _api.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Aurigo.RowManagement.Infrastructure.Persistence.RowDbContext>();
            if (!await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.Tenants, t => t.Id == otherTenant))
            {
                db.Tenants.Add(new Aurigo.RowManagement.Domain.Entities.Tenant { Id = otherTenant, Name = "Other Agency F7", Slug = "other-f7", IsActive = true });
                await db.SaveChangesAsync();
            }
            await scope.ServiceProvider.GetRequiredService<Aurigo.RowManagement.Infrastructure.Persistence.Seed.TenantProvisioner>().EnsureDefaultRolesAsync(otherTenant);
        }
        var mine = await _api.As("Admin");
        var created = await Ok<RoleDto>(await mine.Http.PostAsJsonAsync("/api/v1/roles", new { name = Unique("Mine"), description = (string?)null }), HttpStatusCode.Created);

        var theirs = await _api.As("Admin", tenant: otherTenant);
        (await Roles(theirs)).Should().NotContain(r => r.Id == created.Id);
        (await Users(theirs)).Should().BeEmpty();
        (await theirs.Http.PutAsJsonAsync($"/api/v1/roles/{created.Id}", new { name = "Hijack", description = (string?)null })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.DeleteAsync($"/api/v1/roles/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [PgFact]
    public async Task AT_AD_36_Settings_changes_are_in_the_audit_trail_and_no_password_or_hash_is()
    {
        var admin = await _api.As("Admin");
        var finance = await RoleNamed(admin, "Finance");
        var user = await CreateUserAsync(admin, finance.Id, password: "Audit-Secret-77");
        await admin.Http.PostAsJsonAsync($"/api/v1/users/{user.Id}/reset-password", new { password = "Audit-Secret-88" });

        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where entity_id=@i", ("i", user.Id))).Should().BeGreaterThan(0);
        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from audit_log where new_value like '%Audit-Secret%' or old_value like '%Audit-Secret%'")).Should().Be(0);
        var hash = (string)(await PgTestDatabase.QueryAsync(_api.Database, "select password_hash from app_users where id=@i", ("i", user.Id)))[0][0]!;
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from audit_log where new_value like @h or old_value like @h", ("h", "%" + hash + "%"))).Should().Be(0);
    }
}
