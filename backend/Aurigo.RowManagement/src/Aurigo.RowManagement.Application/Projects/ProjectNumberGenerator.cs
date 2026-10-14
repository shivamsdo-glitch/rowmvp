using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Projects;

public sealed record ProjectNumberFormat(string Pattern, int FiscalYearStartMonth)
{
    public static readonly ProjectNumberFormat Default = new("{FY}-{SEQ:4}", 1);
}

/// <summary>
/// Issues the next free project number from the tenant's format (ROW-001). The counter change is saved with the
/// project, guarded by an optimistic concurrency token, so two simultaneous creates cannot get the same number.
/// </summary>
public sealed class ProjectNumberGenerator
{
    private readonly IRowDbContext _db;
    private readonly TimeProvider _clock;

    public ProjectNumberGenerator(IRowDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ProjectNumberFormat> GetFormatAsync(CancellationToken ct)
    {
        var s = await _db.ProjectNumberSettings.FirstOrDefaultAsync(ct);
        return s is null ? ProjectNumberFormat.Default : new ProjectNumberFormat(s.Pattern, s.FiscalYearStartMonth);
    }

    public int CurrentFiscalYear(ProjectNumberFormat format) =>
        ProjectNumbering.FiscalYearOf(DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime), format.FiscalYearStartMonth);

    /// <summary>The next unused number. Advances the tracked counter; the caller saves it together with the project.</summary>
    public async Task<string> NextAsync(CancellationToken ct)
    {
        var format = await GetFormatAsync(ct);
        var fy = CurrentFiscalYear(format);
        var scope = ProjectNumbering.ScopeKey(format.Pattern, fy);

        var counter = await _db.ProjectNumberCounters.FirstOrDefaultAsync(c => c.ScopeKey == scope, ct);
        if (counter is null)
        {
            counter = new ProjectNumberCounter { ScopeKey = scope };
            _db.ProjectNumberCounters.Add(counter);
        }

        // Skip numbers already taken by hand-typed or imported projects.
        while (true)
        {
            counter.LastValue++;
            var candidate = ProjectNumbering.Render(format.Pattern, fy, counter.LastValue);
            var lower = candidate.ToLower();
            if (!await _db.Projects.AnyAsync(p => p.Number.ToLower() == lower, ct)) return candidate;
        }
    }

    /// <summary>The number the next project would get under this format. Changes nothing.</summary>
    public async Task<string> PeekNextAsync(ProjectNumberFormat format, CancellationToken ct)
    {
        var fy = CurrentFiscalYear(format);
        var scope = ProjectNumbering.ScopeKey(format.Pattern, fy);
        var last = await _db.ProjectNumberCounters.Where(c => c.ScopeKey == scope).Select(c => (int?)c.LastValue).FirstOrDefaultAsync(ct) ?? 0;
        return ProjectNumbering.Render(format.Pattern, fy, last + 1);
    }
}
