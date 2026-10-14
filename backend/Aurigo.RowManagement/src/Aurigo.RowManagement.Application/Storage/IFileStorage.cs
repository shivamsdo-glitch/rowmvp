namespace Aurigo.RowManagement.Application.Storage;

/// <summary>
/// Where file bytes live. The MVP implementation writes to local disk; a cloud or document-platform
/// implementation replaces the registration, not the callers. Keys are opaque and returned by <see cref="SaveAsync"/>.
/// </summary>
public interface IFileStorage
{
    /// <summary>Stores the stream and returns the key to read or delete it later. <paramref name="tenantKey"/> must be a GUID.</summary>
    Task<string> SaveAsync(Stream content, string tenantKey, CancellationToken ct);

    /// <summary>Opens the stored bytes. Throws <see cref="FileNotFoundException"/> if the key is unknown.</summary>
    Task<Stream> OpenAsync(string key, CancellationToken ct);

    Task<bool> ExistsAsync(string key, CancellationToken ct);

    /// <summary>Removes the bytes. Deleting an unknown key is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken ct);
}
