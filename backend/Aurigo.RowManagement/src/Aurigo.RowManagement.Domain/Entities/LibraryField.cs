using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// A field the agency defines once in the Library and forms then use: its name, its type and, for a list, the values a user
/// may choose (CAD number, possession type, interest type and so on). Defaults are attached when an agency is set up.
/// </summary>
public class LibraryField : EntityBase
{
    /// <summary>Stable machine key, unique per agency. System fields have well-known keys (<see cref="LibraryKeys"/>).</summary>
    public string Key { get; set; } = default!;
    public string Label { get; set; } = default!;
    public CustomFieldType FieldType { get; set; } = CustomFieldType.List;
    /// <summary>A field the product itself refers to: its key and type cannot change and it cannot be removed.</summary>
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public List<LibraryValue> Values { get; set; } = new();
}

/// <summary>One choice of a list field. Values are switched off, never deleted, so records that use them stay readable.</summary>
public class LibraryValue : EntityBase
{
    public Guid LibraryFieldId { get; set; }
    public string Value { get; set; } = default!;
    /// <summary>Upper-cased value: the key of the "no two values alike, ignoring case" rule.</summary>
    public string NormalizedValue { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// A person in the Library's Agents list: the people the agency assigns to parcels as designer, surveyor, title researcher, appraiser,
/// negotiator, broker and so on, entered once so forms pick them instead of retyping. Switched off, never deleted.
/// </summary>
public class LibraryAgent : EntityBase
{
    public string Name { get; set; } = default!;
    /// <summary>A value of the <c>agent_type</c> Library list, kept as text so the record stays readable if that value is later switched off.</summary>
    public string AgentType { get; set; } = default!;
    public string? Company { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Upper-cased name|type|company: the key of the "no two the same person for the same job" rule.</summary>
    public string NormalizedKey { get; set; } = default!;
}
