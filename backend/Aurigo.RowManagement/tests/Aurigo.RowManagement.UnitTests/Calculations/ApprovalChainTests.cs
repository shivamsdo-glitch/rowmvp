using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Domain.Common;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Calculations;

// ROW-046, ROW-258, ROW-261, ROW-108
public class ApprovalChainTests
{
    private static readonly ApprovalChainConfig Config = new(new[]
    {
        new ApprovalBand(0m, 10_000m, new[] { RoleNames.Negotiator }),
        new ApprovalBand(10_000m, 100_000m, new[] { RoleNames.ProjectManager, RoleNames.Finance }),
        new ApprovalBand(100_000m, null, new[] { RoleNames.ProjectManager, RoleNames.Finance, RoleNames.Admin }),
    });

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9_999.99, 1)]
    [InlineData(10_000, 2)]      // lower bound inclusive
    [InlineData(99_999.99, 2)]
    [InlineData(100_000, 3)]
    [InlineData(5_000_000, 3)]
    public void The_amount_selects_the_band_and_its_steps(double amount, int steps) =>
        ApprovalChain.Evaluate((decimal)amount, Config).Steps.Should().HaveCount(steps);

    [Fact]
    public void Steps_keep_the_configured_order() =>
        ApprovalChain.Evaluate(50_000m, Config).Steps.Should().Equal(RoleNames.ProjectManager, RoleNames.Finance);

    [Fact]
    public void A_different_agency_config_changes_the_result_with_no_code_change()
    {
        var strict = new ApprovalChainConfig(new[] { new ApprovalBand(0m, null, new[] { RoleNames.Admin }) });

        ApprovalChain.Evaluate(1m, strict).Steps.Should().Equal(RoleNames.Admin);
    }

    [Fact]
    public void A_negative_amount_is_rejected() =>
        FluentActions.Invoking(() => ApprovalChain.Evaluate(-1m, Config)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void An_amount_in_a_gap_is_an_error_not_a_silent_pass()
    {
        var gappy = new ApprovalChainConfig(new[]
        {
            new ApprovalBand(0m, 100m, new[] { RoleNames.Negotiator }),
            new ApprovalBand(200m, null, new[] { RoleNames.Admin }),
        });

        FluentActions.Invoking(() => ApprovalChain.Evaluate(150m, gappy)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Approvals_are_strictly_ordered()
    {
        var req = ApprovalChain.Evaluate(50_000m, Config);

        ApprovalChain.NextApprover(req, Array.Empty<string>()).Should().Be(RoleNames.ProjectManager);
        ApprovalChain.CanApprove(req, Array.Empty<string>(), new[] { RoleNames.Finance }).Should().BeFalse("Finance cannot sign before the Project Manager");
        ApprovalChain.CanApprove(req, new[] { RoleNames.ProjectManager }, new[] { RoleNames.Finance }).Should().BeTrue();
        ApprovalChain.IsComplete(req, new[] { RoleNames.ProjectManager }).Should().BeFalse();
        ApprovalChain.IsComplete(req, new[] { RoleNames.ProjectManager, RoleNames.Finance }).Should().BeTrue();
        ApprovalChain.NextApprover(req, new[] { RoleNames.ProjectManager, RoleNames.Finance }).Should().BeNull();
    }

    [Fact]
    public void A_complete_chain_lets_nobody_sign_again()
    {
        var req = ApprovalChain.Evaluate(1m, Config);

        ApprovalChain.CanApprove(req, new[] { RoleNames.Negotiator }, new[] { RoleNames.Negotiator, RoleNames.Admin }).Should().BeFalse();
    }

    [Fact]
    public void A_good_config_has_no_problems() => ApprovalChain.Validate(Config).Should().BeEmpty();

    private static IReadOnlyList<string> Errors(ApprovalBand band) =>
        ApprovalChain.Validate(new ApprovalChainConfig(new[] { band }));

    [Fact]
    public void Validation_catches_each_kind_of_bad_config()
    {
        ApprovalChain.Validate(new ApprovalChainConfig(Array.Empty<ApprovalBand>())).Should().ContainSingle();

        Errors(new ApprovalBand(5m, null, new[] { RoleNames.Admin })).Should().Contain(e => e.Contains("start at 0"));
        Errors(new ApprovalBand(0m, 100m, new[] { RoleNames.Admin })).Should().Contain(e => e.Contains("no upper limit"));
        Errors(new ApprovalBand(0m, null, Array.Empty<string>())).Should().Contain(e => e.Contains("approver role"));
        Errors(new ApprovalBand(0m, null, new[] { "Wizard" })).Should().Contain(e => e.Contains("unknown role"));
        Errors(new ApprovalBand(0m, null, new[] { RoleNames.Admin, RoleNames.Admin })).Should().Contain(e => e.Contains("twice"));

        var overlap = new ApprovalChainConfig(new[]
        {
            new ApprovalBand(0m, 200m, new[] { RoleNames.Admin }),
            new ApprovalBand(100m, null, new[] { RoleNames.Admin }),
        });
        ApprovalChain.Validate(overlap).Should().Contain(e => e.Contains("overlap"));

        var gap = new ApprovalChainConfig(new[]
        {
            new ApprovalBand(0m, 100m, new[] { RoleNames.Admin }),
            new ApprovalBand(200m, null, new[] { RoleNames.Admin }),
        });
        ApprovalChain.Validate(gap).Should().Contain(e => e.Contains("gap"));
    }
}
