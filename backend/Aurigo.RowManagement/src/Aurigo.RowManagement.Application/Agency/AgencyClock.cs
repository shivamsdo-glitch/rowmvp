using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Agency;

/// <summary>
/// "Today" for the caller's agency, by project. The agency sets a default time zone and any project can override it, so a
/// deadline is counted on the calendar of the place the parcel is in (see <see cref="AgencyTime"/>). Nothing is remembered
/// between calls, so a changed setting is seen at once.
/// </summary>
public sealed class AgencyClock
{
    private readonly IRowDbContext _db;
    private readonly TimeProvider _clock;

    public AgencyClock(IRowDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public DateTimeOffset UtcNow => _clock.GetUtcNow();

    /// <summary>The agency's default zone ("UTC" until an administrator chooses one).</summary>
    public async Task<string> AgencyZoneAsync(CancellationToken ct) =>
        (await _db.AgencySettings.Select(s => s.TimeZoneId).FirstOrDefaultAsync(ct)) ?? AgencyTime.Utc;

    /// <summary>The zone that governs a project: its own, else the agency's.</summary>
    public async Task<string> ZoneOfAsync(Project project, CancellationToken ct) =>
        AgencyTime.Resolve(project.TimeZoneId, await AgencyZoneAsync(ct));

    public async Task<string> ZoneOfProjectAsync(Guid projectId, CancellationToken ct)
    {
        var own = await _db.Projects.Where(p => p.Id == projectId).Select(p => p.TimeZoneId).FirstOrDefaultAsync(ct);
        return AgencyTime.Resolve(own, await AgencyZoneAsync(ct));
    }

    public async Task<DateOnly> TodayForProjectAsync(Guid projectId, CancellationToken ct) =>
        AgencyTime.TodayIn(await ZoneOfProjectAsync(projectId, ct), UtcNow);
}
