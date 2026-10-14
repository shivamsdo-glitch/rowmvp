namespace Aurigo.RowManagement.Domain.Common;

public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    Guid CreatedBy { get; set; }
    DateTime UpdatedAt { get; set; }
    Guid UpdatedBy { get; set; }
}
