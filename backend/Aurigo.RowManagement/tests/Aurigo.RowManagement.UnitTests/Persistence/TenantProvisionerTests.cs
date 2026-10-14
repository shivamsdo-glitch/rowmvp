using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.UnitTests.Persistence;

public class TenantProvisionerTests
{
    private static readonly int ExpectedRows = RoleNames.All.Count + RoleNames.All.Count * PageKeys.All.Count;

    [Fact]
    public async Task Creates_the_six_roles_and_a_permission_row_for_every_role_and_page()
    {
        var store = TestDb.NewStoreName();
        await using var db = TestDb.Create(store, tenant: null);

        var added = await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantA);

        added.Should().Be(ExpectedRows);
        var roles = await db.Roles.IgnoreQueryFilters().Where(r => r.TenantId == TestDb.TenantA).ToListAsync();
        roles.Select(r => r.Name).Should().BeEquivalentTo(RoleNames.All);
        (await db.RolePagePermissions.IgnoreQueryFilters().CountAsync(p => p.TenantId == TestDb.TenantA))
            .Should().Be(RoleNames.All.Count * PageKeys.All.Count);
    }

    [Fact]
    public async Task Running_again_adds_nothing()
    {
        await using var db = TestDb.Create(TestDb.NewStoreName(), tenant: null);
        var provisioner = new TenantProvisioner(db);

        await provisioner.EnsureDefaultRolesAsync(TestDb.TenantA);
        (await provisioner.EnsureDefaultRolesAsync(TestDb.TenantA)).Should().Be(0);

        (await db.Roles.IgnoreQueryFilters().CountAsync()).Should().Be(RoleNames.All.Count);
    }

    [Fact]
    public async Task Stored_flags_match_the_template_and_no_access_rows_are_all_false_Custom()
    {
        await using var db = TestDb.Create(TestDb.NewStoreName(), tenant: null);
        await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantA);

        var rows = await db.RolePagePermissions.IgnoreQueryFilters().Include(p => p.Role).ToListAsync();

        var negotiatorNegotiations = rows.Single(p => p.Role!.Name == RoleNames.Negotiator && p.PageKey == PageKeys.Negotiations);
        negotiatorNegotiations.Should().Match<RolePagePermission>(p =>
            p.Template == PermissionTemplates.Pro && p.CanCreate && p.CanRead && p.CanUpdate && p.CanDelete);

        var financeParcels = rows.Single(p => p.Role!.Name == RoleNames.Finance && p.PageKey == PageKeys.Parcels);
        financeParcels.Should().Match<RolePagePermission>(p =>
            p.Template == PermissionTemplates.Basic && !p.CanCreate && p.CanRead && !p.CanUpdate && !p.CanDelete);

        var noAccess = rows.Single(p => p.Role!.Name == RoleNames.Finance && p.PageKey == PageKeys.Administration);
        noAccess.Should().Match<RolePagePermission>(p =>
            p.Template == PermissionTemplates.Custom && !p.CanCreate && !p.CanRead && !p.CanUpdate && !p.CanDelete);
    }

    [Fact]
    public async Task An_administrators_edits_survive_a_reseed()
    {
        var store = TestDb.NewStoreName();
        await using (var db = TestDb.Create(store, tenant: null))
        {
            await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantA);
            var row = await db.RolePagePermissions.IgnoreQueryFilters().Include(p => p.Role)
                .SingleAsync(p => p.Role!.Name == RoleNames.Finance && p.PageKey == PageKeys.Parcels);
            row.CanUpdate = true;
            row.Template = PermissionTemplates.Custom;
            await db.SaveChangesAsync();
        }

        await using var again = TestDb.Create(store, tenant: null);
        (await new TenantProvisioner(again).EnsureDefaultRolesAsync(TestDb.TenantA)).Should().Be(0);

        var kept = await again.RolePagePermissions.IgnoreQueryFilters().Include(p => p.Role)
            .SingleAsync(p => p.Role!.Name == RoleNames.Finance && p.PageKey == PageKeys.Parcels);
        kept.CanUpdate.Should().BeTrue();
        kept.Template.Should().Be(PermissionTemplates.Custom);
    }

    [Fact]
    public async Task A_missing_permission_row_is_back_filled_without_touching_the_others()
    {
        // Simulates a release that adds a page: existing tenants get the new row with its default.
        var store = TestDb.NewStoreName();
        await using (var db = TestDb.Create(store, tenant: null))
        {
            await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantA);
            var removed = await db.RolePagePermissions.IgnoreQueryFilters().Include(p => p.Role)
                .SingleAsync(p => p.Role!.Name == RoleNames.Appraiser && p.PageKey == PageKeys.Appraisals);
            db.RolePagePermissions.Remove(removed);
            await db.SaveChangesAsync();
        }

        await using var again = TestDb.Create(store, tenant: null);
        (await new TenantProvisioner(again).EnsureDefaultRolesAsync(TestDb.TenantA)).Should().Be(1);

        (await again.RolePagePermissions.IgnoreQueryFilters().Include(p => p.Role)
                .SingleAsync(p => p.Role!.Name == RoleNames.Appraiser && p.PageKey == PageKeys.Appraisals))
            .Template.Should().Be(PermissionTemplates.Pro);
    }

    [Fact]
    public async Task Each_tenant_gets_its_own_separate_copy()
    {
        var store = TestDb.NewStoreName();
        await using var db = TestDb.Create(store, tenant: null);
        var provisioner = new TenantProvisioner(db);

        await provisioner.EnsureDefaultRolesAsync(TestDb.TenantA);
        await provisioner.EnsureDefaultRolesAsync(TestDb.TenantB);

        (await db.Roles.IgnoreQueryFilters().CountAsync()).Should().Be(RoleNames.All.Count * 2);
        var roleIdsA = await db.Roles.IgnoreQueryFilters().Where(r => r.TenantId == TestDb.TenantA).Select(r => r.Id).ToListAsync();
        (await db.RolePagePermissions.IgnoreQueryFilters()
                .Where(p => p.TenantId == TestDb.TenantB && roleIdsA.Contains(p.RoleId)).AnyAsync())
            .Should().BeFalse("tenant B's permissions must hang off tenant B's roles");
    }

    [Fact]
    public async Task Every_provisioned_row_is_audited_under_its_own_tenant()
    {
        var store = TestDb.NewStoreName();
        await using (var db = TestDb.Create(store, tenant: null))
            await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantA);

        await using var read = TestDb.Create(store, tenant: null);
        var audit = await read.AuditLogs.IgnoreQueryFilters().ToListAsync();
        audit.Should().HaveCount(ExpectedRows).And.OnlyContain(a => a.TenantId == TestDb.TenantA);
    }
}
