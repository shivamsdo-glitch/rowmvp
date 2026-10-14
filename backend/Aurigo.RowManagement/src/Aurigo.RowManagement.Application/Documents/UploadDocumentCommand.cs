using System.Security.Cryptography;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.ExternalServices;
using Aurigo.RowManagement.Application.Mappers;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;

namespace Aurigo.RowManagement.Application.Documents;

/// <summary>
/// Attaches a file to any registered ROW entity. Flow: validate, check the page's Update permission,
/// store the bytes in the external document store, then record the metadata row. The caller owns
/// <see cref="Content"/> and disposes it.
/// </summary>
public sealed record UploadDocumentCommand(
    string EntityType,
    Guid EntityId,
    string FileName,
    long Length,
    Stream Content,
    string? DocType,
    string? Description) : IRequest<DocumentDto>;

public sealed class UploadDocumentCommandValidator : AbstractValidator<UploadDocumentCommand>
{
    public UploadDocumentCommandValidator(IOptions<DocumentOptions> options)
    {
        var max = options.Value.MaxBytes;

        RuleFor(x => x.EntityType)
            .Must(AttachmentTargets.IsKnown)
            .WithMessage($"Files can be attached to: {string.Join(", ", AttachmentTargets.EntityTypes)}.");
        RuleFor(x => x.EntityId).NotEmpty();
        RuleFor(x => x.FileName)
            .NotEmpty()
            .MaximumLength(260)
            .Must(n => DocumentFileTypes.ContentTypeFor(n) is not null)
            .WithMessage($"This file type is not allowed. Allowed: {string.Join(", ", DocumentFileTypes.AllowedExtensions)}.");
        RuleFor(x => x.Length)
            .GreaterThan(0).WithMessage("The file is empty.")
            .LessThanOrEqualTo(max).WithMessage($"The file is larger than the {max / (1024 * 1024)} MB limit.");
        RuleFor(x => x.DocType).MaximumLength(60);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class UploadDocumentCommandHandler : IRequestHandler<UploadDocumentCommand, DocumentDto>
{
    private readonly IRowDbContext _db;
    private readonly IDocMgmtClient _store;
    private readonly IPagePermissionService _permissions;
    private readonly IOptions<DocumentOptions> _options;

    public UploadDocumentCommandHandler(
        IRowDbContext db, IDocMgmtClient store, IPagePermissionService permissions, IOptions<DocumentOptions> options)
    {
        _db = db;
        _store = store;
        _permissions = permissions;
        _options = options;
    }

    public async Task<DocumentDto> Handle(UploadDocumentCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(AttachmentTargets.PageFor(request.EntityType)!, PermissionAction.Update, ct);

        var fileName = DocumentFileTypes.SanitizeFileName(request.FileName);
        var contentType = DocumentFileTypes.ContentTypeFor(fileName)
                          ?? throw new InvalidRequestException("file_type_not_allowed", "This file type is not allowed.");

        // The declared length was validated, but a client can lie: measure what actually arrived, never reading
        // more than the limit plus one byte. (Buffered because the MVP stores and hashes in memory; a real
        // document store would take a stream.)
        var max = _options.Value.MaxBytes;
        using var buffer = new MemoryStream();
        var copied = await CopyAtMostAsync(request.Content, buffer, max + 1, ct);
        if (copied == 0)
            throw new InvalidRequestException("file_empty", "The file is empty.");
        if (copied > max)
            throw new InvalidRequestException("file_too_large", $"The file is larger than the {max / (1024 * 1024)} MB limit.");

        buffer.Position = 0;
        var hash = Convert.ToBase64String(SHA256.HashData(buffer));
        buffer.Position = 0;

        var stored = await _store.UploadAsync(
            buffer, fileName, contentType, request.EntityType, request.EntityId, request.DocType, ct);

        var document = new Document
        {
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = copied,
            ContentHash = hash,
            DocType = string.IsNullOrWhiteSpace(request.DocType) ? null : request.DocType.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ExternalDocId = stored.DocId,
        };
        _db.Documents.Add(document);
        await _db.SaveChangesAsync(ct);

        return document.ToDto();
    }

    private static async Task<long> CopyAtMostAsync(Stream source, Stream destination, long limit, CancellationToken ct)
    {
        var chunk = new byte[81920];
        long total = 0;
        while (total < limit)
        {
            var read = await source.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - total)), ct);
            if (read == 0) break;
            await destination.WriteAsync(chunk.AsMemory(0, read), ct);
            total += read;
        }
        return total;
    }
}
