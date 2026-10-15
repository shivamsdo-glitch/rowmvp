using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Domain.Common;

/// <summary>Well-known Library field keys that the rest of the product refers to.</summary>
public static class LibraryKeys
{
    /// <summary>The agency's manually-set parcel status list; its values are the parcel statuses themselves.</summary>
    public const string ParcelStatus = "parcel_status";
    public const string InterestType = "interest_type";
    public const string PossessionType = "possession_type";
    public const string AgentType = "agent_type";
    public const string ContactType = "contact_type";
    public const string CadNumber = "cad_number";
    public const string Segment = "segment";
    public const string Priority = "priority";
}

public sealed record DefaultLibraryField(string Key, string Label, CustomFieldType Type, IReadOnlyList<string> Values);

/// <summary>
/// The Library an agency starts with. Every value is the agency's to change; none is a legal requirement. Where an agency has
/// not configured anything, these are what forms offer. The parcel status values live in <see cref="DefaultParcelRules"/>.
/// </summary>
public static class DefaultLibrary
{
    public static readonly IReadOnlyList<DefaultLibraryField> Fields = new DefaultLibraryField[]
    {
        new(LibraryKeys.ParcelStatus, "Parcel status", CustomFieldType.List, Array.Empty<string>()),
        new(LibraryKeys.InterestType, "Interest type", CustomFieldType.List, new[]
        {
            "Sidewalk Easement", "Aerial Easement", "Permanent Easement", "Long-Term Lease", "Austin Energy Easement", "Public Utility Easement",
            "Drainage Easement", "Temporary Construction Easement", "Partial Fee", "Full Fee",
        }),
        new(LibraryKeys.PossessionType, "Possession type", CustomFieldType.List, new[] { "PUA", "Closing", "Notice of Deposit", "Judgement" }),
        new(LibraryKeys.AgentType, "Agent type", CustomFieldType.List, new[] { "Appraiser" }),
        new(LibraryKeys.ContactType, "Contact type", CustomFieldType.List, new[] { "Broker" }),
        new(LibraryKeys.CadNumber, "CAD number", CustomFieldType.Text, Array.Empty<string>()),
        new(LibraryKeys.Segment, "Segment", CustomFieldType.Text, Array.Empty<string>()),
        new(LibraryKeys.Priority, "Priority", CustomFieldType.Text, Array.Empty<string>()),
    };
}
