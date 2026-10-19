using Aurigo.RowManagement.Application.ExternalServices;

namespace Aurigo.RowManagement.Infrastructure.ExternalClients;

/// <summary>Stand-in for the Planned Projects and Build Projects modules: a fixed set of sample records.</summary>
public sealed class StubProjectCatalogClient : IProjectCatalogClient
{
    private static readonly CatalogProject[] Sample =
    {
        new(CatalogSources.Planned, "PLN-2026-001", "Harbor Corridor Improvements"),
        new(CatalogSources.Planned, "PLN-2026-002", "Maple Avenue Extension"),
        new(CatalogSources.Planned, "PLN-2027-004", "SR-20 Widening Phase 2"),
        new(CatalogSources.Planned, "PLN-2027-009", "Riverside Trail Connector"),
        new(CatalogSources.Build, "BLD-2025-017", "Harbor Bridge Replacement"),
        new(CatalogSources.Build, "BLD-2025-021", "Pier Lane Reconstruction"),
        new(CatalogSources.Build, "BLD-2026-003", "Fifth Street Signal Upgrade"),
    };

    public Task<IReadOnlyList<CatalogProject>> SearchAsync(string source, string? search, CancellationToken ct)
    {
        var term = search?.Trim();
        IReadOnlyList<CatalogProject> rows = Sample
            .Where(p => p.Source == source)
            .Where(p => string.IsNullOrEmpty(term) || p.Number.Contains(term, StringComparison.OrdinalIgnoreCase) || p.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Number)
            .ToList();
        return Task.FromResult(rows);
    }

    public Task<CatalogProject?> FindAsync(string source, string number, CancellationToken ct) =>
        Task.FromResult(Sample.FirstOrDefault(p => p.Source == source && string.Equals(p.Number, number.Trim(), StringComparison.OrdinalIgnoreCase)));
}
