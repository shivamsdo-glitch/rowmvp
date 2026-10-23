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
        new(LibraryKeys.ContactType, "Contact type", CustomFieldType.List, new[] { "Broker", "Attorney", "Tenant", "Family member", "Property manager", "Lender", "Representative" }),
        new(LibraryKeys.CadNumber, "CAD number", CustomFieldType.Text, Array.Empty<string>()),
        new(LibraryKeys.Segment, "Segment", CustomFieldType.Text, Array.Empty<string>()),
        new(LibraryKeys.Priority, "Priority", CustomFieldType.Text, Array.Empty<string>()),
        // Lists the parcel forms use (Phase 1.5). Starter values for every list, so no dropdown opens empty; each is the agency's to edit.
        new(LibraryKeys.OwnerType, "Owner type", CustomFieldType.List, new[] { "CAD Owner", "Commercial Sign Owner", "Fee Owner", "Prior Owner", "Vested Owner" }),
        new(LibraryKeys.RelocationType, "Relocation type", CustomFieldType.List, new[] { "Residential", "Non-residential", "Personal Property only" }),
        new(LibraryKeys.EncumbranceType, "Encumbrance type", CustomFieldType.List, new[]
        {
            "Mortgage Lien", "Easement - Access", "Mechanic Lien", "Financing Statements (UCC-1)", "Judgment", "Agreement", "Restrictive Covenants",
            "Deed/Use Restrictions", "Lease", "Encroachment", "License", "Reservation", "Other", "Easement - Public Utility", "Easement - Private Utility",
            "Easement - Other", "Deed of Trust",
        }),
        new("cure_status", "Cure status", CustomFieldType.List, new[]
        {
            "Not started", "In progress", "Cured", "Waived", "Cannot be cured",
        }),
        new("appraisal_type", "Appraisal type", CustomFieldType.List, new[]
        {
            "Full Appraisal", "Waiver Valuation", "Before and After", "Appraisal Update", "Review Appraisal",
        }),
        new("appraisal_amount_type", "Appraisal amount type", CustomFieldType.List, new[]
        {
            "Land", "Improvements", "Damages", "Cost to cure", "Temporary easement", "Permanent easement", "Fixtures", "Site improvements", "Total just compensation",
        }),
        new("review_type", "Appraisal review type", CustomFieldType.List, new[]
        {
            "Technical review", "Desk review", "Field review", "Administrative review", "Final review",
        }),
        new("review_recommendation", "Review recommendation", CustomFieldType.List, new[]
        {
            "Approved", "Approved with changes", "Returned for revision", "Rejected",
        }),
        new("mode_of_contact", "Mode of contact", CustomFieldType.List, new[]
        {
            "Phone", "In person", "Email", "Letter", "Certified mail", "Text message", "Site visit",
        }),
        new("easement_type", "Easement type", CustomFieldType.List, new[]
        {
            "Permanent Easement", "Temporary Construction Easement", "Drainage Easement", "Public Utility Easement", "Access Easement", "Slope Easement", "Sidewalk Easement", "Aerial Easement",
        }),
        new("offer_type", "Offer type", CustomFieldType.List, new[]
        {
            "Initial Offer", "Revised Offer", "Final Offer", "Administrative Settlement Offer", "Counteroffer",
        }),
        new("settlement_type", "Settlement type", CustomFieldType.List, new[]
        {
            "Administrative", "Negotiated", "Mediated", "Court-approved",
        }),
        new("counteroffer_response", "Counteroffer response", CustomFieldType.List, new[]
        {
            "Accepted", "Rejected", "Countered", "Under review", "No response",
        }),
        new("jat_response", "JAT response", CustomFieldType.List, new[]
        {
            "Approved", "Rejected", "Countered", "Under review", "No response",
        }),
        new("demo_status", "Demo status", CustomFieldType.List, new[]
        {
            "Not started", "Bid out", "Scheduled", "In progress", "Completed", "On hold",
        }),
        new("disposition_type", "Disposition type", CustomFieldType.List, new[]
        {
            "Sale", "Exchange", "Lease", "Transfer to another agency", "Donation", "Retention for public use",
        }),
        new("disposition_method", "Disposition method", CustomFieldType.List, new[]
        {
            "Public auction", "Sealed bid", "Negotiated sale", "Direct transfer", "Listing with a broker",
        }),
        new("disposition_status", "Disposition status", CustomFieldType.List, new[]
        {
            "Not started", "Pending approval", "Approved", "Listed", "Under contract", "Complete",
        }),
        new("displacee_status", "Displacee status", CustomFieldType.List, new[]
        {
            "Owner-occupant", "Tenant-occupant", "Owner (not occupant)", "Business owner", "Business tenant", "Nonprofit organization", "Farm operation",
        }),
        new("displacee_interest", "Displacee interest", CustomFieldType.List, new[]
        {
            "Fee owner", "Tenant", "Life estate holder", "Contract purchaser", "Business operator", "Billboard owner",
        }),
        new("eligibility_type", "Relocation eligibility type", CustomFieldType.List, new[]
        {
            "Residential owner (180-day)", "Residential tenant (90-day)", "Business", "Farm", "Nonprofit", "Personal property only", "Billboard / sign",
        }),
        new("relocation_payment_type", "Relocation payment type", CustomFieldType.List, new[]
        {
            "Moving expense", "Replacement housing - purchase supplement", "Replacement housing - rental assistance", "Down payment assistance",
            "Reestablishment expense", "Fixed payment", "Advance payment", "Other",
        }),
        new("conveyance_type", "Conveyance type", CustomFieldType.List, new[]
        {
            "Warranty Deed", "Special Warranty Deed", "Quitclaim Deed", "Deed of Dedication", "Easement Deed", "Temporary Construction Easement", "Right-of-Way Deed", "Judgment",
        }),
        new("fund_type", "Fund type", CustomFieldType.List, new[]
        {
            "Federal", "State", "Local", "Bond proceeds", "Agency general fund", "Grant",
        }),
        new("title_company", "Title company", CustomFieldType.List, new[]
        {
            "Lone Star Title", "Heritage Title Co.", "Capitol Abstract", "Gulf Coast Title", "Stewart Title", "First American Title",
        }),
        new("address_type", "Address type", CustomFieldType.List, new[] { "Tax Card Mailing Address", "Mailing Address", "Property Address", "Billing Address", "Previous Address" }),
        new("document_folder_structure", "Document folder structure", CustomFieldType.List, new[]
        {
            "By parcel number", "By project phase", "By document type", "By owner name",
        }),
        new("document_properties", "Document properties", CustomFieldType.List, new[]
        {
            "Parcel number", "Owner name", "Document type", "Document date", "Recording number", "Version",
        }),
    };
}
