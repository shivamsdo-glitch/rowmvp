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
    public const string OwnerType = "owner_type";
    public const string RelocationType = "relocation_type";
    public const string EncumbranceType = "encumbrance_type";
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
        new(LibraryKeys.AgentType, "Agent type", CustomFieldType.List, new[]
        {
            "Project Lead", "Row Designer", "Surveyor", "Title Researcher", "Appraisal Coordinator", "Appraiser", "Appraisal Reviewer", "Negotiator", "Broker",
            "Counsel", "Relocation", "FHWA Representative", "Land Planner", "Community Engagement", "Site Work",
        }),
        new(LibraryKeys.ContactType, "Contact type", CustomFieldType.List, new[] { "Broker" }),
        new(LibraryKeys.CadNumber, "CAD number", CustomFieldType.Text, Array.Empty<string>()),
        new(LibraryKeys.Segment, "Segment", CustomFieldType.Text, Array.Empty<string>()),
        new(LibraryKeys.Priority, "Priority", CustomFieldType.Text, Array.Empty<string>()),
        // Lists the parcel forms use (Phase 1.5). The three below carry the values the reference screens showed; the rest start empty
        // for the agency to fill (U: "empty, except where the screens show values").
        new(LibraryKeys.OwnerType, "Owner type", CustomFieldType.List, new[] { "CAD Owner", "Commercial Sign Owner", "Fee Owner", "Prior Owner", "Vested Owner" }),
        new(LibraryKeys.RelocationType, "Relocation type", CustomFieldType.List, new[] { "Residential", "Non-residential", "Personal Property only" }),
        new(LibraryKeys.EncumbranceType, "Encumbrance type", CustomFieldType.List, new[]
        {
            "Mortgage Lien", "Easement - Access", "Mechanic Lien", "Financing Statements (UCC-1)", "Judgment", "Agreement", "Restrictive Covenants",
            "Deed/Use Restrictions", "Lease", "Encroachment", "License", "Reservation", "Other", "Easement - Public Utility", "Easement - Private Utility",
            "Easement - Other", "Deed of Trust",
        }),
        new("cure_status", "Cure status", CustomFieldType.List, Array.Empty<string>()),
        new("appraisal_type", "Appraisal type", CustomFieldType.List, Array.Empty<string>()),
        new("appraisal_amount_type", "Appraisal amount type", CustomFieldType.List, Array.Empty<string>()),
        new("review_type", "Appraisal review type", CustomFieldType.List, Array.Empty<string>()),
        new("review_recommendation", "Review recommendation", CustomFieldType.List, Array.Empty<string>()),
        new("mode_of_contact", "Mode of contact", CustomFieldType.List, Array.Empty<string>()),
        new("easement_type", "Easement type", CustomFieldType.List, Array.Empty<string>()),
        new("offer_type", "Offer type", CustomFieldType.List, Array.Empty<string>()),
        new("settlement_type", "Settlement type", CustomFieldType.List, Array.Empty<string>()),
        new("counteroffer_response", "Counteroffer response", CustomFieldType.List, Array.Empty<string>()),
        new("jat_response", "JAT response", CustomFieldType.List, Array.Empty<string>()),
        new("demo_status", "Demo status", CustomFieldType.List, Array.Empty<string>()),
        new("disposition_type", "Disposition type", CustomFieldType.List, Array.Empty<string>()),
        new("disposition_method", "Disposition method", CustomFieldType.List, Array.Empty<string>()),
        new("disposition_status", "Disposition status", CustomFieldType.List, Array.Empty<string>()),
        new("displacee_status", "Displacee status", CustomFieldType.List, Array.Empty<string>()),
        new("displacee_interest", "Displacee interest", CustomFieldType.List, Array.Empty<string>()),
        new("eligibility_type", "Relocation eligibility type", CustomFieldType.List, Array.Empty<string>()),
        new("relocation_payment_type", "Relocation payment type", CustomFieldType.List, new[]
        {
            "Moving expense", "Replacement housing - purchase supplement", "Replacement housing - rental assistance", "Down payment assistance",
            "Reestablishment expense", "Fixed payment", "Advance payment", "Other",
        }),
        new("conveyance_type", "Conveyance type", CustomFieldType.List, Array.Empty<string>()),
        new("fund_type", "Fund type", CustomFieldType.List, Array.Empty<string>()),
        new("title_company", "Title company", CustomFieldType.List, Array.Empty<string>()),
        new("address_type", "Address type", CustomFieldType.List, new[] { "Tax Card Mailing Address" }),
        new("document_folder_structure", "Document folder structure", CustomFieldType.List, Array.Empty<string>()),
        new("document_properties", "Document properties", CustomFieldType.List, Array.Empty<string>()),
    };
}
