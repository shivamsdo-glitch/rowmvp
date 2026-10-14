namespace Aurigo.RowManagement.Application.Documents;

/// <summary>What the API shows about an attachment. The external store's id is deliberately not exposed.</summary>
public sealed record DocumentDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? DocType,
    string? Description,
    DateTime UploadedAt,
    Guid UploadedBy);

/// <summary>A file ready to stream back to the caller. The caller disposes <see cref="Content"/>.</summary>
public sealed record DocumentContent(Stream Content, string FileName, string ContentType, long SizeBytes);
