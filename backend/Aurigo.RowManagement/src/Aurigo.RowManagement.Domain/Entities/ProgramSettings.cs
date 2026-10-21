using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// The thresholds the ROW Director's Program view judges projects by (ADR 0024, 0025). One row per tenant; a tenant with no row uses the defaults,
/// which are the values written here. An administrator changes them in Configuration.
/// </summary>
public class ProgramSettings : EntityBase
{
    /// <summary>A parcel that has been on one step longer than this many days is "stuck".</summary>
    public int StuckDays { get; set; } = 30;
    /// <summary>A project whose ROW clear date is this close, with too little closed, is on watch.</summary>
    public int ClearDateWarningDays { get; set; } = 90;
    public int ClearDateWarningPercentClosed { get; set; } = 75;
    /// <summary>A project is off track on overdue deadlines when at least this many parcels, and at least <see cref="RedOverduePercent"/> of the project, are overdue.</summary>
    public int RedOverdueMinimumParcels { get; set; } = 3;
    public int RedOverduePercent { get; set; } = 25;
}

/// <summary>
/// Which team owns a workflow step, where the agency has chosen differently from the default mapping. Only differences are stored, so a step with no
/// row follows the default. Unique per tenant and step key.
/// </summary>
public class ProgramStepTeam : EntityBase
{
    public string StepKey { get; set; } = default!;
    public string Team { get; set; } = default!;
}
