using System.Collections.Concurrent;
using Aurigo.RowManagement.Application.ExternalServices;
using Microsoft.Extensions.Logging;

namespace Aurigo.RowManagement.Infrastructure.ExternalClients;

/// <summary>
/// In-memory stand-in for a future document management system. Keeps uploaded bytes for the life of
/// the process so upload, list and download behave realistically in demos and tests. Register as a singleton.
/// </summary>
public sealed class StubDocMgmtClient : IDocMgmtClient
{
    private readonly ConcurrentDictionary<string, (DocReference Ref, byte[] Bytes)> _store = new();
    private readonly ILogger<StubDocMgmtClient> _log;

    public StubDocMgmtClient(ILogger<StubDocMgmtClient> log) => _log = log;

    public async Task<DocReference> UploadAsync(
        Stream content, string fileName, string contentType,
        string? entityType, Guid? entityId, string? docType, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var id = Guid.NewGuid().ToString("N");
        var docRef = new DocReference(id, fileName, contentType, bytes.LongLength, DateTime.UtcNow, entityType, entityId, docType);
        _store[id] = (docRef, bytes);

        _log.LogInformation("[StubDocMgmt] Uploaded {FileName} as {DocId} for {EntityType} {EntityId} ({Size} bytes)",
            fileName, id, entityType ?? "-", entityId?.ToString() ?? "-", bytes.Length);
        return docRef;
    }

    public Task<Stream> DownloadAsync(string docId, CancellationToken ct) =>
        Task.FromResult<Stream>(_store.TryGetValue(docId, out var entry) ? new MemoryStream(entry.Bytes) : Stream.Null);

    public Task<IReadOnlyList<DocReference>> ListByEntityAsync(string entityType, Guid entityId, CancellationToken ct)
    {
        IReadOnlyList<DocReference> list = _store.Values
            .Where(v => v.Ref.EntityType == entityType && v.Ref.EntityId == entityId)
            .Select(v => v.Ref)
            .OrderByDescending(d => d.UploadedAt)
            .ToArray();
        return Task.FromResult(list);
    }
}
