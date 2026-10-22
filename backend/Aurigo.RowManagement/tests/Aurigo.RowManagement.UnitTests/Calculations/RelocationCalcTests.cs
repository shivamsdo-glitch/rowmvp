using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Domain.Common;
using FluentAssertions;
using Xunit;

namespace Aurigo.RowManagement.UnitTests.Calculations;

/// <summary>ADR 0026: the pure relocation rules. Matches ROW-085, ROW-095, ROW-097, AT-RA-42, AT-RA-43.</summary>
public class RelocationCalcTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();

    [Fact]
    public void AT_RA_42_No_displaced_interests_means_the_rule_does_not_apply()
    {
        var p = RelocationCalc.Progress(Array.Empty<Guid>(), new[] { new RelocationProgressInput(A, new DateOnly(2026, 1, 1)) });
        p.AllComplete.Should().BeFalse();
        p.Flagged.Should().Be(0);
    }

    [Fact]
    public void AT_RA_42_Every_displaced_interest_needs_a_completed_relocation()
    {
        var rows = new[] { new RelocationProgressInput(A, new DateOnly(2026, 1, 1)), new RelocationProgressInput(B, null) };
        var p = RelocationCalc.Progress(new[] { A, B }, rows);
        p.Should().Be(new RelocationProgress(2, 1));
        p.AllComplete.Should().BeFalse();
        RelocationCalc.Progress(new[] { A, B }, rows.Append(new RelocationProgressInput(B, new DateOnly(2026, 2, 1)))).AllComplete.Should().BeTrue();
    }

    [Fact]
    public void AT_RA_42_A_relocation_not_linked_to_an_interest_counts_for_nothing()
    {
        RelocationCalc.Progress(new[] { A }, new[] { new RelocationProgressInput(null, new DateOnly(2026, 1, 1)) }).AllComplete.Should().BeFalse();
    }

    [Fact]
    public void ROW_095_The_claim_period_ends_eighteen_months_after_the_displacee_vacated()
    {
        RelocationCalc.ClaimDeadline(new DateOnly(2026, 3, 31)).Should().Be(new DateOnly(2027, 9, 30));
        RelocationCalc.ClaimDeadline(null).Should().BeNull();
        RelocationCalc.ClaimExpiring(new DateOnly(2026, 1, 1), null, new DateOnly(2027, 6, 20), 30).Should().BeTrue();
        RelocationCalc.ClaimExpiring(new DateOnly(2026, 1, 1), null, new DateOnly(2026, 6, 20), 30).Should().BeFalse();
        RelocationCalc.ClaimExpiring(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2027, 6, 20), 30).Should().BeFalse();
    }

    [Fact]
    public void Stage_follows_the_diagram_from_the_dates_recorded()
    {
        var d = new DateOnly(2026, 1, 1);
        RelocationCalc.Stage(null, null, null, null, null, null, null, null, false).Should().Be("Not started");
        RelocationCalc.Stage(d, null, null, null, null, null, null, null, false).Should().Be("GIN sent");
        RelocationCalc.Stage(d, d, d, d, d, d, null, null, false).Should().Be("Awaiting move");
        RelocationCalc.Stage(d, d, d, d, d, d, d, null, false).Should().Be("Vacated: close out");
        RelocationCalc.Stage(d, d, d, d, d, d, d, d, false).Should().Be("Complete");
        RelocationCalc.Stage(d, null, null, null, null, null, null, null, true).Should().Be("Ineligible");
    }

    [Fact]
    public void ROW_097_The_default_payment_chain_is_valid_and_larger_payments_need_Finance()
    {
        ApprovalChain.Validate(RelocationCalc.DefaultPaymentChain).Should().BeEmpty();
        ApprovalChain.Evaluate(1_000m, RelocationCalc.DefaultPaymentChain).Steps.Should().Equal(RoleNames.ProjectManager);
        ApprovalChain.Evaluate(25_000m, RelocationCalc.DefaultPaymentChain).Steps.Should().Equal(RoleNames.ProjectManager, RoleNames.Finance);
        RelocationCalc.ParseRoles("ProjectManager, Finance").Should().Equal("ProjectManager", "Finance");
    }
}
