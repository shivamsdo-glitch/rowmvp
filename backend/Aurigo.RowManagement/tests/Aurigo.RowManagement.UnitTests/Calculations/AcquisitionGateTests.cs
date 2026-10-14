using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Domain.Enums;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Calculations;

/// <summary>ROW-076, ROW-238, ROW-072: NEPA and authorization gate on acquisition, by funding source.</summary>
public class AcquisitionGateTests
{
    private static AcquisitionGateResult Run(OversightType o, bool nepa, bool auth, bool exception) =>
        AcquisitionGate.Evaluate(new AcquisitionGateInput(o, nepa, auth, exception));

    [Fact]
    public void ROW_076_Local_funds_are_not_gated()
    {
        var r = Run(OversightType.Local, nepa: false, auth: false, exception: false);
        r.Unlocked.Should().BeTrue();
        r.Reason.Should().Be(AcquisitionGateReason.UngatedFunding);
    }

    [Theory]
    [InlineData(OversightType.Federal)]
    [InlineData(OversightType.State)]
    public void ROW_076_Gated_funding_needs_both_nepa_and_an_acquisition_authorization(OversightType o)
    {
        Run(o, true, true, false).Should().Match<AcquisitionGateResult>(r =>
            r.Unlocked && r.Reason == AcquisitionGateReason.ClearanceComplete);
        Run(o, true, false, false).Missing.Should().Equal(AcquisitionGate.MissingAuthorization);
        Run(o, false, true, false).Missing.Should().Equal(AcquisitionGate.MissingNepa);
        var neither = Run(o, false, false, false);
        neither.Unlocked.Should().BeFalse();
        neither.Missing.Should().Equal(AcquisitionGate.MissingNepa, AcquisitionGate.MissingAuthorization);
    }

    [Fact]
    public void ROW_076_An_approved_exception_unlocks_gated_funding_even_with_nothing_on_file()
    {
        var r = Run(OversightType.Federal, false, false, exception: true);
        r.Unlocked.Should().BeTrue();
        r.Reason.Should().Be(AcquisitionGateReason.ExceptionApproved);
    }

    [Fact]
    public void ROW_076_Full_clearance_wins_over_an_exception_in_the_reason()
    {
        Run(OversightType.Federal, true, true, exception: true).Reason.Should().Be(AcquisitionGateReason.ClearanceComplete);
    }

    [Fact]
    public void ROW_076_Which_funding_types_are_gated_is_data_not_code()
    {
        var onlyFederal = new HashSet<OversightType> { OversightType.Federal };
        var input = new AcquisitionGateInput(OversightType.State, false, false, false);

        AcquisitionGate.Evaluate(input, onlyFederal).Unlocked.Should().BeTrue();
        AcquisitionGate.Evaluate(input).Unlocked.Should().BeFalse("state oversight is gated by default");
    }

    [Theory]
    [InlineData(AuthorizationType.Preliminary, false)]
    [InlineData(AuthorizationType.Acquisition, true)]
    [InlineData(AuthorizationType.Supplemental, true)]
    public void ROW_238_Preliminary_authorization_covers_appraisal_and_title_only(AuthorizationType t, bool counts) =>
        AcquisitionGate.CountsForAcquisition(t).Should().Be(counts);
}
