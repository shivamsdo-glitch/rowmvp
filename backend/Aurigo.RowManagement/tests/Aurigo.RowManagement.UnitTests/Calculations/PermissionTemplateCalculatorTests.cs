using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Calculations;

public class PermissionTemplateCalculatorTests
{
    [Fact]
    public void Basic_is_read_only()
    {
        PermissionTemplateCalculator.FlagsFor(PermissionTemplates.Basic)
            .Should().Be(new PermissionFlags(Create: false, Read: true, Update: false, Delete: false));
    }

    [Fact]
    public void Standard_is_create_read_update_without_delete()
    {
        PermissionTemplateCalculator.FlagsFor(PermissionTemplates.Standard)
            .Should().Be(new PermissionFlags(true, true, true, false));
    }

    [Fact]
    public void Pro_is_everything()
    {
        PermissionTemplateCalculator.FlagsFor(PermissionTemplates.Pro)
            .Should().Be(new PermissionFlags(true, true, true, true));
    }

    [Theory]
    [InlineData(PermissionTemplates.Custom)]
    [InlineData("Enterprise")]
    [InlineData("basic")]   // labels are case-sensitive
    [InlineData("")]
    [InlineData(null)]
    public void Custom_unknown_and_missing_templates_grant_nothing(string? template)
    {
        PermissionTemplateCalculator.FlagsFor(template).Should().Be(PermissionFlags.None);
    }

    [Theory]
    [InlineData(PermissionTemplates.Basic)]
    [InlineData(PermissionTemplates.Standard)]
    [InlineData(PermissionTemplates.Pro)]
    public void A_template_round_trips_through_its_flags(string template)
    {
        var flags = PermissionTemplateCalculator.FlagsFor(template);
        PermissionTemplateCalculator.TemplateFor(flags).Should().Be(template);
    }

    [Theory]
    [InlineData(false, false, false, false)] // no access
    [InlineData(false, true, true, false)]   // update without create
    [InlineData(false, false, false, true)]  // delete only
    [InlineData(true, false, false, false)]  // create without read
    [InlineData(true, true, false, true)]    // delete without update
    public void Any_other_combination_is_labelled_Custom(bool c, bool r, bool u, bool d)
    {
        PermissionTemplateCalculator.TemplateFor(new PermissionFlags(c, r, u, d))
            .Should().Be(PermissionTemplates.Custom);
    }

    [Fact]
    public void A_user_with_several_roles_gets_the_union_of_their_grants()
    {
        var merged = PermissionTemplateCalculator.Merge(new[]
        {
            new PermissionFlags(false, true, false, false),  // read from one role
            new PermissionFlags(true, false, false, false),  // create from another
        });

        merged.Should().Be(new PermissionFlags(true, true, false, false));
    }

    [Fact]
    public void Merging_nothing_grants_nothing()
    {
        PermissionTemplateCalculator.Merge(Array.Empty<PermissionFlags>()).Should().Be(PermissionFlags.None);
    }

    [Fact]
    public void A_role_that_denies_never_removes_what_another_role_grants()
    {
        PermissionTemplateCalculator.Merge(new[] { PermissionFlags.None, PermissionTemplateCalculator.FlagsFor(PermissionTemplates.Pro) })
            .Should().Be(PermissionTemplateCalculator.FlagsFor(PermissionTemplates.Pro));
    }

    [Theory]
    [InlineData(PermissionAction.Create, false, true)]
    [InlineData(PermissionAction.Read, true, true)]
    [InlineData(PermissionAction.Update, false, true)]
    [InlineData(PermissionAction.Delete, false, false)]
    public void Allows_checks_the_matching_flag(PermissionAction action, bool basic, bool standard)
    {
        PermissionTemplateCalculator.FlagsFor(PermissionTemplates.Basic).Allows(action).Should().Be(basic);
        PermissionTemplateCalculator.FlagsFor(PermissionTemplates.Standard).Allows(action).Should().Be(standard);
    }
}
