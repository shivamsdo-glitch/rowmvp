using Aurigo.RowManagement.Application.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Aurigo.RowManagement.Infrastructure.ExternalClients;

public sealed class FileStorageOptions
{
    public const string Section = "Storage";

    /// <summary>Root folder for stored files. Relative paths resolve against the app's content root.</summary>
    public string RootPath { get; set; } = "App_Data/files";
}

/// <summary>
/// Stores each file under <c>{root}/{tenant}/{yyyy}/{guid}</c>. File names from users never touch the
/// path: keys are server-generated, and every resolved path is checked to stay inside the root.
/// </summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalDiskFileStorage(IOptions<FileStorageOptions> options, IHostEnvironment env)
    {
        var path = options.Value.RootPath;
        _root = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(env.ContentRootPath, path));
    }

    public async Task<string> SaveAsync(Stream content, string tenantKey, CancellationToken ct)
    {
        if (!Guid.TryParse(tenantKey, out var tenant))
            throw new ArgumentException("Tenant key must be a GUID.", nameof(tenantKey));

        var key = $"{tenant:N}/{DateTime.UtcNow:yyyy}/{Guid.NewGuid():N}";
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, ct);
        return key;
    }

    public Task<Stream> OpenAsync(string key, CancellationToken ct)
    {
        var path = Resolve(key);
        if (!File.Exists(path)) throw new FileNotFoundException("Stored file not found.", key);
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(File.Exists(Resolve(key)));

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = Resolve(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid storage key.", nameof(key));
        return full;
    }
}
