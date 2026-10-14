namespace Aurigo.RowManagement.Application.ExternalServices;

/// <summary>
/// Future external document management system (vault ROW-157..164, ROW-161). No real client exists
/// in the MVP: only <c>StubDocMgmtClient</c> is registered. Replace the registration, not the callers.
/// </summary>
public interface IDocMgmtClient
{
    /// <summary>Upload a file. Pass entityType + entityId so ListByEntityAsync can find it later.</summary>
    Task<DocReference> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string? entityType,
        Guid? entityId,
        string? docType,
        CancellationToken ct);

    Task<Stream> DownloadAsync(string docId, CancellationToken ct);

    Task<IReadOnlyList<DocReference>> ListByEntityAsync(string entityType, Guid entityId, CancellationToken ct);
}

public sealed record DocReference(
    string DocId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime UploadedAt,
    string? EntityType = null,
    Guid? EntityId = null,
    string? DocType = null);
