using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// Metadata for one file attached to any ROW entity (a title commitment on a title record, an
/// appraisal report on an appraisal, a signed agreement ...). The bytes live in the external
/// document store behind <c>IDocMgmtClient</c>; this row is the tenant-scoped source of truth for
/// what is attached where. Soft-deleted rather than removed: right-of-way files are subject to
/// federal audit, so the row and the stored bytes are kept.
/// </summary>
public class Document : EntityBase
{
    /// <summary>The kind of entity attached to, e.g. "Parcel" (see Application/Documents/AttachmentTargets).</summary>
    public string EntityType { get; set; } = default!;
    public Guid EntityId { get; set; }

    public string FileName { get; set; } = default!;
    public string ContentType { get; set; } = default!;
    public long SizeBytes { get; set; }

    /// <summary>Base64 SHA-256 of the content, so integrity can be shown to an auditor.</summary>
    public string ContentHash { get; set; } = default!;

    /// <summary>Free-form category within the owning module, e.g. "TitleCommitment", "AppraisalReport".</summary>
    public string? DocType { get; set; }
    public string? Description { get; set; }

    /// <summary>Id of the file in the external document store.</summary>
    public string ExternalDocId { get; set; } = default!;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
