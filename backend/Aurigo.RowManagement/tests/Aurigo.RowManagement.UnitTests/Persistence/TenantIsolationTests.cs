using Aurigo.RowManagement.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.UnitTests.Persistence;

public class TenantIsolationTests
{
    private static AppUser NewUser(string email) =>
        new() { FirstName = "T", LastName = "U", Email = email };

    [Fact]
    public async Task Each_tenant_sees_only_its_own_rows()
    {
        var store = TestDb.NewStoreName();

        await using (var a = TestDb.Create(store, TestDb.TenantA))
        {
            a.AppUsers.Add(NewUser("a@row.local"));
            await a.SaveChangesAsync();
        }
        await using (var b = TestDb.Create(store, TestDb.TenantB))
        {
            b.AppUsers.Add(NewUser("b@row.local"));
            await b.SaveChangesAsync();
        }

        await using var readAsA = TestDb.Create(store, TestDb.TenantA);
        await using var readAsB = TestDb.Create(store, TestDb.TenantB);

        (await readAsA.AppUsers.Select(u => u.Email).ToListAsync()).Should().Equal("a@row.local");
        (await readAsB.AppUsers.Select(u => u.Email).ToListAsync()).Should().Equal("b@row.local");
    }

    [Fact]
    public async Task No_tenant_context_fails_closed_and_sees_nothing()
    {
        var store = TestDb.NewStoreName();
        await using (var a = TestDb.Create(store, TestDb.TenantA))
        {
            a.AppUsers.Add(NewUser("a@row.local"));
            await a.SaveChangesAsync();
        }

        await using var anonymous = TestDb.Create(store, tenant: null);
        (await anonymous.AppUsers.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Ignoring_query_filters_is_the_only_way_to_cross_tenants()
    {
        var store = TestDb.NewStoreName();
        await using (var a = TestDb.Create(store, TestDb.TenantA))
        {
            a.AppUsers.Add(NewUser("a@row.local"));
            await a.SaveChangesAsync();
        }

        await using var asB = TestDb.Create(store, TestDb.TenantB);
        (await asB.AppUsers.ToListAsync()).Should().BeEmpty();
        (await asB.AppUsers.IgnoreQueryFilters().ToListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task The_filter_covers_every_tenant_owned_set_including_the_audit_log()
    {
        var store = TestDb.NewStoreName();
        await using (var a = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            a.AppUsers.Add(NewUser("a@row.local"));
            await a.SaveChangesAsync();
        }

        await using var asB = TestDb.Create(store, TestDb.TenantB);
        (await asB.AuditLogs.ToListAsync()).Should().BeEmpty();
        (await asB.Roles.ToListAsync()).Should().BeEmpty();
        (await asB.UserTokens.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Tenants_themselves_are_not_filtered_so_login_can_look_them_up()
    {
        var store = TestDb.NewStoreName();
        await using (var seed = TestDb.Create(store, TestDb.TenantA))
        {
            seed.Tenants.Add(new Tenant { Id = TestDb.TenantA, Name = "A", Slug = "a" });
            await seed.SaveChangesAsync();
        }

        await using var anonymous = TestDb.Create(store, tenant: null);
        (await anonymous.Tenants.ToListAsync()).Should().ContainSingle();
    }
}
