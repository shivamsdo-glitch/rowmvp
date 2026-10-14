using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.ExternalServices;
using Aurigo.RowManagement.Application.Mappers;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Documents;

// ── List ────────────────────────────────────────────────────────────────

/// <summary>The live (not deleted) attachments of one entity, newest first.</summary>
public sealed record ListDocumentsQuery(string EntityType, Guid EntityId, string? DocType = null)
    : IRequest<IReadOnlyList<DocumentDto>>;

public sealed class ListDocumentsQueryValidator : AbstractValidator<ListDocumentsQuery>
{
    public ListDocumentsQueryValidator()
    {
        RuleFor(x => x.EntityType).Must(AttachmentTargets.IsKnown)
            .WithMessage($"Entity type must be one of: {string.Join(", ", AttachmentTargets.EntityTypes)}.");
        RuleFor(x => x.EntityId).NotEmpty();
        RuleFor(x => x.DocType).MaximumLength(60);
    }
}

public sealed class ListDocumentsQueryHandler : IRequestHandler<ListDocumentsQuery, IReadOnlyList<DocumentDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public ListDocumentsQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<DocumentDto>> Handle(ListDocumentsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(AttachmentTargets.PageFor(request.EntityType)!, PermissionAction.Read, ct);

        // Tenant-filtered by the global query filter, so another agency's attachments never appear.
        var docs = await _db.Documents
            .Where(d => d.EntityType == request.EntityType && d.EntityId == request.EntityId && !d.IsDeleted
                        && (request.DocType == null || d.DocType == request.DocType))
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return docs.Select(d => d.ToDto()).ToList();
    }
}

// ── Download ────────────────────────────────────────────────────────────

public sealed record DownloadDocumentQuery(Guid DocumentId) : IRequest<DocumentContent>;

public sealed class DownloadDocumentQueryHandler : IRequestHandler<DownloadDocumentQuery, DocumentContent>
{
    private readonly IRowDbContext _db;
    private readonly IDocMgmtClient _store;
    private readonly IPagePermissionService _permissions;

    public DownloadDocumentQueryHandler(IRowDbContext db, IDocMgmtClient store, IPagePermissionService permissions)
    {
        _db = db;
        _store = store;
        _permissions = permissions;
    }

    public async Task<DocumentContent> Handle(DownloadDocumentQuery request, CancellationToken ct)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == request.DocumentId && !d.IsDeleted, ct)
                  ?? throw new NotFoundException("Document", request.DocumentId);

        await _permissions.EnsureAsync(
            AttachmentTargets.PageFor(doc.EntityType) ?? PageKeys.Administration, PermissionAction.Read, ct);

        var content = await _store.DownloadAsync(doc.ExternalDocId, ct);
        if (ReferenceEquals(content, Stream.Null))
            throw new NotFoundException("Document content", request.DocumentId);

        return new DocumentContent(content, doc.FileName, doc.ContentType, doc.SizeBytes);
    }
}

// ── Delete ──────────────────────────────────────────────────────────────

/// <summary>
/// Removes an attachment from view. The metadata row is flagged deleted and the stored bytes are kept,
/// because right-of-way files are subject to federal audit; the change is recorded in the audit log.
/// </summary>
public sealed record DeleteDocumentCommand(Guid DocumentId) : IRequest;

public sealed class DeleteDocumentCommandHandler : IRequestHandler<DeleteDocumentCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentUser _user;

    public DeleteDocumentCommandHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _user = user;
    }

    public async Task Handle(DeleteDocumentCommand request, CancellationToken ct)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == request.DocumentId && !d.IsDeleted, ct)
                  ?? throw new NotFoundException("Document", request.DocumentId);

        await _permissions.EnsureAsync(
            AttachmentTargets.PageFor(doc.EntityType) ?? PageKeys.Administration, PermissionAction.Delete, ct);

        doc.IsDeleted = true;
        doc.DeletedAt = DateTime.UtcNow;
        doc.DeletedBy = _user.Id;
        await _db.SaveChangesAsync(ct);
    }
}
