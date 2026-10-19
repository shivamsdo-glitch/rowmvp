using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Auth;

/// <summary>Guards the shipped default permission matrix against accidental edits.</summary>
public class DefaultRolePermissionsTests
{
    private static PermissionFlags Flags(string role, string page) =>
        PermissionTemplateCalculator.FlagsFor(DefaultRolePermissions.TemplateFor(role, page));

    [Fact]
    public void Page_keys_are_unique_lower_case_and_non_empty()
    {
        PageKeys.All.Should().OnlyHaveUniqueItems().And.OnlyContain(k => k.Length > 0 && k == k.ToLowerInvariant());
    }

    [Fact]
    public void Every_role_has_a_description()
    {
        foreach (var role in RoleNames.All)
            DefaultRolePermissions.DescriptionFor(role).Should().NotBeNullOrWhiteSpace(role);
    }

    [Fact]
    public void Every_listed_template_is_a_real_template_on_a_real_page()
    {
        foreach (var role in RoleNames.All)
            foreach (var page in PageKeys.All)
            {
                var t = DefaultRolePermissions.TemplateFor(role, page);
                if (t is not null)
                    new[] { PermissionTemplates.Basic, PermissionTemplates.Standard, PermissionTemplates.Pro }
                        .Should().Contain(t, $"{role}/{page}");
            }
    }

    [Fact]
    public void Admin_has_full_access_to_every_page()
    {
        foreach (var page in PageKeys.All)
            Flags(RoleNames.Admin, page).Should().Be(PermissionTemplateCalculator.FlagsFor(PermissionTemplates.Pro), page);
    }

    [Fact]
    public void Only_Admin_can_see_administration()
    {
        foreach (var role in RoleNames.All.Where(r => r != RoleNames.Admin))
            Flags(role, PageKeys.Administration).Read.Should().BeFalse(role);
    }

    [Fact]
    public void Every_role_can_at_least_read_the_dashboard_and_projects()
    {
        foreach (var role in RoleNames.All)
        {
            Flags(role, PageKeys.Dashboard).Read.Should().BeTrue(role);
            Flags(role, PageKeys.Projects).Read.Should().BeTrue(role);
        }
    }

    [Fact]
    public void No_non_admin_role_can_delete_on_a_page_it_does_not_own()
    {
        // Pro (which includes delete) is reserved for a role's own module; spot-check the rest are not Pro.
        Flags(RoleNames.TitleResearcher, PageKeys.Parcels).Delete.Should().BeFalse();
        Flags(RoleNames.Appraiser, PageKeys.Title).Delete.Should().BeFalse();
        Flags(RoleNames.Negotiator, PageKeys.Appraisals).Delete.Should().BeFalse();
        Flags(RoleNames.Finance, PageKeys.Agreements).Delete.Should().BeFalse();
    }

    [Fact]
    public void Each_role_owns_its_module_and_cannot_write_into_another()
    {
        Flags(RoleNames.TitleResearcher, PageKeys.Title).Update.Should().BeTrue();
        Flags(RoleNames.TitleResearcher, PageKeys.Survey).Update.Should().BeTrue();
        Flags(RoleNames.TitleResearcher, PageKeys.Negotiations).Read.Should().BeFalse();

        Flags(RoleNames.Appraiser, PageKeys.Appraisals).Update.Should().BeTrue();
        Flags(RoleNames.Appraiser, PageKeys.Title).Update.Should().BeFalse();

        Flags(RoleNames.Negotiator, PageKeys.Negotiations).Update.Should().BeTrue();
        Flags(RoleNames.Negotiator, PageKeys.Agreements).Update.Should().BeTrue();
        Flags(RoleNames.Negotiator, PageKeys.Title).Update.Should().BeFalse();

        Flags(RoleNames.Finance, PageKeys.Payments).Update.Should().BeTrue();
        Flags(RoleNames.Finance, PageKeys.Parcels).Update.Should().BeFalse();

        Flags(RoleNames.ProjectManager, PageKeys.Projects).Update.Should().BeTrue();
        Flags(RoleNames.ProjectManager, PageKeys.Parcels).Update.Should().BeTrue();
    }

    [Fact]
    public void Approvers_can_act_in_the_approvals_inbox_but_negotiators_only_watch()
    {
        Flags(RoleNames.ProjectManager, PageKeys.Approvals).Update.Should().BeTrue();
        Flags(RoleNames.Appraiser, PageKeys.Approvals).Update.Should().BeTrue();
        Flags(RoleNames.Finance, PageKeys.Approvals).Update.Should().BeTrue();
        Flags(RoleNames.Negotiator, PageKeys.Approvals).Update.Should().BeFalse();
    }

    [Fact]
    public void An_unknown_role_or_page_has_no_template()
    {
        DefaultRolePermissions.TemplateFor("SuperUser", PageKeys.Parcels).Should().BeNull();
        DefaultRolePermissions.TemplateFor(RoleNames.Admin, "nonsense").Should().BeNull();
    }
}
