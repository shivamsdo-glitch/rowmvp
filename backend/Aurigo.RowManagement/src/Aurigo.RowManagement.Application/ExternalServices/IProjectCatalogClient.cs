namespace Aurigo.RowManagement.Application.ExternalServices;

/// <summary>
/// The Planned Projects and Build Projects modules of the wider platform, which a ROW project is linked to. No real client exists in the
/// MVP (ADR 0016: a real client needs a decision from the user): only <c>StubProjectCatalogClient</c> is registered.
/// </summary>
public interface IProjectCatalogClient
{
    /// <summary>Projects of one source ("Planned" or "Build"), optionally filtered by a text that appears in the number or name.</summary>
    Task<IReadOnlyList<CatalogProject>> SearchAsync(string source, string? search, CancellationToken ct);

    /// <summary>One project by its number, or null when the source has none.</summary>
    Task<CatalogProject?> FindAsync(string source, string number, CancellationToken ct);
}

public sealed record CatalogProject(string Source, string Number, string Name);

public static class CatalogSources
{
    public const string Planned = "Planned";
    public const string Build = "Build";
    public static readonly IReadOnlyList<string> All = new[] { Planned, Build };
    public static bool IsKnown(string? source) => source is not null && All.Contains(source, StringComparer.Ordinal);
}
