using Aurigo.RowManagement.Application.Documents;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

/// <summary>Multipart form for attaching a file. The field names are the form keys.</summary>
public sealed class UploadDocumentForm
{
    /// <summary>The kind of entity to attach to, e.g. "Parcel", "TitleRecord", "Appraisal".</summary>
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? DocType { get; set; }
    public string? Description { get; set; }
    public IFormFile? File { get; set; }
}

/// <summary>
/// The one attachment API every ROW module uses: upload, list, download and delete files on any entity
/// registered in <see cref="AttachmentTargets"/>. Access follows the owning page's permissions
/// (Read to list/download, Update to attach, Delete to remove).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly IMediator _mediator;
    public DocumentsController(IMediator mediator) => _mediator = mediator;

    /// <summary>The live attachments of one entity, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string entityType, [FromQuery] Guid entityId, [FromQuery] string? docType, CancellationToken ct) =>
        Ok(await _mediator.Send(new ListDocumentsQuery(entityType, entityId, docType), ct));

    /// <summary>Attaches a file (multipart/form-data) to an entity.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload([FromForm] UploadDocumentForm form, CancellationToken ct)
    {
        if (form.File is null || form.File.Length == 0)
            throw new FluentValidation.ValidationException(new[] { new ValidationFailure("File", "A non-empty file is required.") });

        await using var stream = form.File.OpenReadStream();
        var created = await _mediator.Send(new UploadDocumentCommand(
            form.EntityType, form.EntityId, form.File.FileName, form.File.Length, stream, form.DocType, form.Description), ct);

        return Created($"/api/v1/documents/{created.Id}/content", created);
    }

    /// <summary>Downloads the file. Always sent as an attachment and never sniffed, so it cannot render in the browser.</summary>
    [HttpGet("{id:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var file = await _mediator.Send(new DownloadDocumentQuery(id), ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>Removes the attachment from view. The record and stored file are kept for audit.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteDocumentCommand(id), ct);
        return NoContent();
    }
}
