using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// A person on a parcel's team and the role they hold (ROW-019). The person is free text because consultants and agency staff
/// are both assigned here and the product has no staff directory yet.
/// </summary>
public class ParcelTeamMember : EntityBase
{
    public Guid ParcelId { get; set; }
    public TeamRole Role { get; set; }
    public string Name { get; set; } = default!;
    /// <summary>Upper-cased name: with the parcel and role, the key of the "no one twice in one role" rule.</summary>
    public string NameKey { get; set; } = default!;
    public string? Organization { get; set; }
    /// <summary>A consultant rather than agency staff.</summary>
    public bool IsConsultant { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
}

/// <summary>One logged contact with an owner or other party about a parcel: who, when, how, and what was said (ROW-020).</summary>
public class ParcelDiaryEntry : EntityBase
{
    public Guid ParcelId { get; set; }
    public DateOnly OccurredOn { get; set; }
    public DiaryMethod Method { get; set; }
    public DiaryDirection Direction { get; set; }
    /// <summary>Who was contacted, as written when the entry was made.</summary>
    public string ContactName { get; set; } = default!;
    /// <summary>The party record, when the person is one.</summary>
    public Guid? PartyId { get; set; }
    public string Summary { get; set; } = default!;
    /// <summary>What was promised or answered.</summary>
    public string? Outcome { get; set; }
}

/// <summary>A free-text note on a parcel. Who made it and when come from the audit columns (ROW-021).</summary>
public class ParcelNote : EntityBase
{
    public Guid ParcelId { get; set; }
    public string Text { get; set; } = default!;
}
