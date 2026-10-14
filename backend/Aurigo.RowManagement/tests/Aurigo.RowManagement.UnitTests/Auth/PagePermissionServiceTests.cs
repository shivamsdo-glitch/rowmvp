using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Auth;

public class PagePermissionServiceTests
{
    /// <summary>A provisioned tenant A and a service acting as a user with the given roles in <paramref name="tenant"/>.</summary>
    private static async Task<PagePermissionService> ServiceFor(string store, Guid tenant, params string[] roles)
    {
        await using (var seed = TestDb.Create(store, tenant: null))
            await new TenantProvisioner(seed).EnsureDefaultRolesAsync(TestDb.TenantA);

        var db = TestDb.Create(store, tenant, TestDb.UserA);
        return new PagePermissionService(db, new FakeUser(TestDb.UserA, roles));
    }

    [Fact]
    public async Task A_role_may_do_what_its_template_allows()
    {
        var svc = await ServiceFor(TestDb.NewStoreName(), TestDb.TenantA, RoleNames.Negotiator);

        await svc.EnsureAsync(PageKeys.Negotiations, PermissionAction.Update);
        await svc.EnsureAsync(PageKeys.Negotiations, PermissionAction.Delete);
        await svc.EnsureAsync(PageKeys.Title, PermissionAction.Read);
    }

    [Fact]
    public async Task A_role_is_refused_what_its_template_withholds()
    {
        var svc = await ServiceFor(TestDb.NewStoreName(), TestDb.TenantA, RoleNames.Negotiator);

        var update = () => svc.EnsureAsync(PageKeys.Title, PermissionAction.Update);
        await update.Should().ThrowAsync<ForbiddenException>().WithMessage("*update*title*");

        var anything = () => svc.EnsureAsync(PageKeys.Administration, PermissionAction.Read);
        await anything.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_user_with_several_roles_gets_the_union()
    {
        var svc = await ServiceFor(TestDb.NewStoreName(), TestDb.TenantA, RoleNames.Finance, RoleNames.TitleExaminer);

        await svc.EnsureAsync(PageKeys.Payments, PermissionAction.Update);  // from Finance
        await svc.EnsureAsync(PageKeys.Title, PermissionAction.Update);     // from TitleExaminer
        var neither = () => svc.EnsureAsync(PageKeys.Appraisals, PermissionAction.Update);
        await neither.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_user_with_no_roles_can_do_nothing()
    {
        var svc = await ServiceFor(TestDb.NewStoreName(), TestDb.TenantA);

        var act = () => svc.EnsureAsync(PageKeys.Dashboard, PermissionAction.Read);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task A_role_name_does_not_carry_over_to_a_tenant_where_it_was_never_defined()
    {
        // Admin in tenant B (which has no roles provisioned) gets nothing, even though tenant A's Admin is all-powerful.
        var svc = await ServiceFor(TestDb.NewStoreName(), TestDb.TenantB, RoleNames.Admin);

        var act = () => svc.EnsureAsync(PageKeys.Administration, PermissionAction.Read);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Effective_permissions_list_every_page_in_order_with_the_combined_flags()
    {
        var svc = await ServiceFor(TestDb.NewStoreName(), TestDb.TenantA, RoleNames.Appraiser);

        var all = await svc.GetEffectiveAsync();

        all.Select(p => p.PageKey).Should().Equal(PageKeys.All);
        all.Single(p => p.PageKey == PageKeys.Appraisals).Should()
            .Be(new PagePermissionDto(PageKeys.Appraisals, true, true, true, true));
        all.Single(p => p.PageKey == PageKeys.Title).Should()
            .Be(new PagePermissionDto(PageKeys.Title, false, true, false, false));
        all.Single(p => p.PageKey == PageKeys.Administration).Should()
            .Be(new PagePermissionDto(PageKeys.Administration, false, false, false, false));
    }

    [Fact]
    public async Task Effective_permissions_for_a_user_with_no_roles_are_all_denied_but_still_list_every_page()
    {
        var svc = await ServiceFor(TestDb.NewStoreName(), TestDb.TenantA);

        var all = await svc.GetEffectiveAsync();

        all.Should().HaveCount(PageKeys.All.Count)
            .And.OnlyContain(p => !p.CanCreate && !p.CanRead && !p.CanUpdate && !p.CanDelete);
    }
}
