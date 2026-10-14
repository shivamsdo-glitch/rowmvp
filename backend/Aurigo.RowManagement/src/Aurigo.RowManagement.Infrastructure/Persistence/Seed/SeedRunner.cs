using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Seed;

/// <summary>One idempotent unit of startup seeding.</summary>
public interface ISeedStep
{
    string Name { get; }

    /// <summary>Lower runs first. Steps depend on earlier ones (tenant, then roles, then users, then data).</summary>
    int Order { get; }

    /// <summary>True for demo data that must never be created outside Development.</summary>
    bool DevelopmentOnly { get; }

    Task RunAsync(CancellationToken ct);
}

/// <summary>
/// Runs after the database is migrated, in order. Platform steps (default roles and permissions) run in
/// every environment; demo steps run only in Development. Every step is idempotent, so running at each
/// startup is safe. A failing step stops startup: a half-seeded database should not serve requests.
/// </summary>
public sealed class SeedRunner
{
    private readonly IReadOnlyList<ISeedStep> _steps;
    private readonly IHostEnvironment _env;
    private readonly ILogger<SeedRunner> _log;

    public SeedRunner(IEnumerable<ISeedStep> steps, IHostEnvironment env, ILogger<SeedRunner> log)
    {
        _steps = steps.OrderBy(s => s.Order).ToList();
        _env = env;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        foreach (var step in _steps)
        {
            if (step.DevelopmentOnly && !_env.IsDevelopment())
            {
                _log.LogDebug("Seed step '{Step}' skipped (development only).", step.Name);
                continue;
            }

            try
            {
                _log.LogInformation("Seed step '{Step}' running.", step.Name);
                await step.RunAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Seed step '{Step}' failed.", step.Name);
                throw;
            }
        }
    }
}
