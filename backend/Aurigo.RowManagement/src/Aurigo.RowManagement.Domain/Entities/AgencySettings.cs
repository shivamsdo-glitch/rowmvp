using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// Settings that apply to a whole agency (tenant). One row per tenant; when absent the defaults apply.
/// </summary>
public class AgencySettings : EntityBase
{
    /// <summary>
    /// The IANA time zone in which the agency counts days (deadlines, "due today", "completed on"). A project can override it,
    /// because an agency can span more than one zone. "UTC" until an administrator chooses.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";
}
