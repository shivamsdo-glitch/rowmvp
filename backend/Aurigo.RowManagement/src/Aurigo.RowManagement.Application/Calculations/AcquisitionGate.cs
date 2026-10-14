using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Application.Calculations;

public enum AcquisitionGateReason
{
    /// <summary>The funding source is not gated (for example local funds); agency rules apply.</summary>
    UngatedFunding = 1,
    /// <summary>NEPA decision and an acquisition authorization are both on file.</summary>
    ClearanceComplete = 2,
    /// <summary>An early, hardship or protective acquisition has been approved.</summary>
    ExceptionApproved = 3,
    /// <summary>Gated funding and something is missing; see <see cref="AcquisitionGateResult.Missing"/>.</summary>
    Blocked = 4,
}

public sealed record AcquisitionGateInput(
    OversightType Oversight,
    bool NepaDecisionRecorded,
    bool AcquisitionAuthorizationOnFile,
    bool ExceptionApproved);

public sealed record AcquisitionGateResult(bool Unlocked, AcquisitionGateReason Reason, IReadOnlyList<string> Missing);

/// <summary>
/// Whether offers and negotiations may start for a project (Diagram 1, ROW-076, ROW-072). Title, survey and
/// preliminary appraisal are never held by this gate.
/// </summary>
public static class AcquisitionGate
{
    public const string MissingNepa = "NEPA decision";
    public const string MissingAuthorization = "Acquisition authorization";

    /// <summary>The oversight types the gate applies to unless the agency configures otherwise (Diagram 1: federal or state).</summary>
    public static readonly IReadOnlySet<OversightType> DefaultGated = new HashSet<OversightType>
    {
        OversightType.Federal, OversightType.State,
    };

    public static AcquisitionGateResult Evaluate(AcquisitionGateInput input, IReadOnlySet<OversightType> gated)
    {
        if (!gated.Contains(input.Oversight))
            return new AcquisitionGateResult(true, AcquisitionGateReason.UngatedFunding, Array.Empty<string>());

        if (input.NepaDecisionRecorded && input.AcquisitionAuthorizationOnFile)
            return new AcquisitionGateResult(true, AcquisitionGateReason.ClearanceComplete, Array.Empty<string>());

        if (input.ExceptionApproved)
            return new AcquisitionGateResult(true, AcquisitionGateReason.ExceptionApproved, Array.Empty<string>());

        var missing = new List<string>();
        if (!input.NepaDecisionRecorded) missing.Add(MissingNepa);
        if (!input.AcquisitionAuthorizationOnFile) missing.Add(MissingAuthorization);
        return new AcquisitionGateResult(false, AcquisitionGateReason.Blocked, missing);
    }

    public static AcquisitionGateResult Evaluate(AcquisitionGateInput input) => Evaluate(input, DefaultGated);

    /// <summary>Preliminary authorizations cover appraisal and title only, so only these types unlock negotiations.</summary>
    public static bool CountsForAcquisition(AuthorizationType type) => type != AuthorizationType.Preliminary;
}
