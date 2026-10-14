using Aurigo.RowManagement.Application.Documents;
using Aurigo.RowManagement.Domain.Entities;

namespace Aurigo.RowManagement.Application.Mappers;

public static class DocumentMapper
{
    public static DocumentDto ToDto(this Document d) => new(
        d.Id, d.EntityType, d.EntityId, d.FileName, d.ContentType, d.SizeBytes,
        d.DocType, d.Description, d.CreatedAt, d.CreatedBy);
}
