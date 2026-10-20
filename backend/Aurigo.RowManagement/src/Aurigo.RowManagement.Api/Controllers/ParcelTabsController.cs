using Aurigo.RowManagement.Application.ParcelTabs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

public sealed record ContactLogBody(
    DateTime ContactedAt, string? Mode, string? ContactedBy, string? ContactPerson, string? Notes, List<ContactLogLinkInput>? Links);
public sealed record EasementBody(
    string? Number, string? EasementType, string? Description, DateOnly? StartDate, DateOnly? EndDate, string? StartStation, string? EndStation);

public sealed record OwnershipBody(
    string? OwnerName, string? OwnerType, List<OwnershipAddressInput>? Addresses, List<OwnershipContactInput>? Contacts);

public sealed record EncumbranceBody(
    string? EncumbranceType, string? DocumentTitle, DateOnly? DocumentDate, DateOnly? DateRecorded, string? DocumentNumber, bool IsUnrecorded,
    string? Grantor, string? Grantee, DateOnly? ExceptionSubmittedDate, DateOnly? ExceptionApprovedDate, string? ImpactAnalysis, string? RecommendedCure,
    string? CureStatus, string? ReviewNotes, List<ContactLogLinkInput>? Links);

public sealed record TitleClosingEnvelope(TitleClosingDto? Record);
public sealed record TitleClosingBody(
    string? AssignedTo, string? TitleCompany, string? TitleClosingAgent, string? GfNumber, string? FinalTitlePolicyNo, DateOnly? PsaSignedByOwnerDate,
    DateOnly? PsaSignedByAtpDate, decimal? PsaAmount, DateOnly? SettlementReceivedDate, DateOnly? SettlementExecutedDate, bool CurativeItemsCompleted,
    DateOnly? ClosingPackageSubmittedDate, DateOnly? ClosingPackageApprovedDate, DateOnly? ClosingInstructionDate, DateOnly? AnticipatedClosingDate,
    DateOnly? ActualClosingDate, List<ConveyanceDocumentInput>? ConveyanceDocuments, List<TitleCommitmentInput>? TitleCommitments);

public sealed record AppraisalBody(
    string? AppraisalType, string? Appraiser, bool LandPlannerRequired, string? AssignedTo, DateOnly? RequestedDate, DateOnly? ReceivedDate, DateOnly? ReportDate,
    DateOnly? DueDate, DateOnly? SubmittedToAtpDate, DateOnly? EffectiveDate, List<AppraisalAmountInput>? Amounts, string? ReviewAppraiser, string? ReviewType,
    DateOnly? AppraisalReceivedDate, decimal? ApprovedJustCompensation, DateOnly? ReviewCompletedDate, DateOnly? ReviewSubmittedDate, string? ReviewRecommendation,
    decimal? ReviewRecommendedValue, DateOnly? SubmittedToJatDate, DateOnly? ApprovedByJatDate, bool FtaConcurrenceNeeded);

public sealed record QuestionnaireBody(
    string? RelocationType, DateOnly? DueByDate, DateOnly? SentToOwnerDate, List<QuestionnaireInterestInput>? Interests, List<QuestionnaireQuestionInput>? Questions);

public sealed record CondemnationEnvelope(CondemnationDto? Record);
public sealed record CondemnationBody(
    CondemnationDetails? Details, List<StatusNoteInput>? StatusNotes, List<CondemnationAttorneyInput>? Attorneys, List<CondemnationCommissionerInput>? Commissioners,
    List<CondemnationNoticeInput>? Notices);
public sealed record PropertyEnvelope(PropertyDto? Record);
public sealed record PropertyBody(PropertyDetails? Details, List<StatusNoteInput>? StatusNotes, List<PropertyFundingInput>? Funding);
public sealed record RelocationBody(
    RelocationDetails? Details, List<StatusNoteInput>? StatusNotes, List<RelocationAgentInput>? Agents, List<RelocationContactInput>? Contacts,
    List<RelocationEligibilityInput>? Eligibilities, List<RelocationContactLogInput>? ContactLog);

public sealed record NegotiationEnvelope(NegotiationDto? Record);
public sealed record NegotiationBody(
    string? AssignedTo, decimal? TotalAgreementAmount, string? Notes, DateOnly? CounterofferSubmittedToJatDate, DateOnly? CounterofferResponseFromJatDate, string? JatResponse,
    bool FtaApprovalRequiredForCounteroffer, DateOnly? PuaRequestedDate, DateOnly? PuaApprovedDate, decimal? PuaApprovedAmount, DateOnly? PuaExecutedDate, DateOnly? PuaClosedDate,
    List<NegotiationNoteInput>? StatusNotes, List<NegotiationAgentInput>? Agents, List<NegotiationRightOfEntryInput>? RightsOfEntry, List<NegotiationOfferInput>? Offers,
    List<NegotiationSettlementInput>? Settlements);

/// <summary>The Contact Log, Easements, Ownership Details, Title Encumbrances, Title and Closing, Appraisal, Negotiation, Questionnaire, Condemnation, Property Management and Relocation tabs of a parcel (Phase 1.5, ADR 0017). Each is governed by its own page's permissions.</summary>
[ApiController]
[Authorize]
[Route("api/v1/parcels/{parcelId:guid}")]
public sealed class ParcelTabsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ParcelTabsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("contact-log")]
    [ProducesResponseType(typeof(IReadOnlyList<ContactLogEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ContactLog(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListContactLogQuery(parcelId), ct));

    [HttpPost("contact-log")]
    [ProducesResponseType(typeof(ContactLogEntryDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddContactLog(Guid parcelId, [FromBody] ContactLogBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/contact-log", await _mediator.Send(new AddContactLogEntryCommand(parcelId, b.ContactedAt, b.Mode, b.ContactedBy, b.ContactPerson, b.Notes, b.Links), ct));

    [HttpPut("contact-log/{entryId:guid}")]
    [ProducesResponseType(typeof(ContactLogEntryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateContactLog(Guid parcelId, Guid entryId, [FromBody] ContactLogBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateContactLogEntryCommand(parcelId, entryId, b.ContactedAt, b.Mode, b.ContactedBy, b.ContactPerson, b.Notes, b.Links), ct));

    [HttpDelete("contact-log/{entryId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveContactLog(Guid parcelId, Guid entryId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveContactLogEntryCommand(parcelId, entryId), ct);
        return NoContent();
    }

    [HttpGet("ownerships")]
    [ProducesResponseType(typeof(IReadOnlyList<OwnershipDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ownerships(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListOwnershipsQuery(parcelId), ct));

    [HttpPost("ownerships")]
    [ProducesResponseType(typeof(OwnershipDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddOwnership(Guid parcelId, [FromBody] OwnershipBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/ownerships", await _mediator.Send(new AddOwnershipCommand(parcelId, b.OwnerName ?? "", b.OwnerType, b.Addresses, b.Contacts), ct));

    [HttpPut("ownerships/{ownershipId:guid}")]
    [ProducesResponseType(typeof(OwnershipDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateOwnership(Guid parcelId, Guid ownershipId, [FromBody] OwnershipBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateOwnershipCommand(parcelId, ownershipId, b.OwnerName ?? "", b.OwnerType, b.Addresses, b.Contacts), ct));

    [HttpDelete("ownerships/{ownershipId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveOwnership(Guid parcelId, Guid ownershipId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveOwnershipCommand(parcelId, ownershipId), ct);
        return NoContent();
    }

    /// <summary>The parcel's title and closing record: <c>{ "record": null }</c> when none has been saved yet.</summary>
    [HttpGet("appraisals")]
    [ProducesResponseType(typeof(IReadOnlyList<AppraisalDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Appraisals(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListAppraisalsQuery(parcelId), ct));

    [HttpPost("appraisals")]
    [ProducesResponseType(typeof(AppraisalDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddAppraisal(Guid parcelId, [FromBody] AppraisalBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/appraisals", await _mediator.Send(new AddAppraisalCommand(
            parcelId, b.AppraisalType, b.Appraiser, b.LandPlannerRequired, b.AssignedTo, b.RequestedDate, b.ReceivedDate, b.ReportDate, b.DueDate, b.SubmittedToAtpDate,
            b.EffectiveDate, b.Amounts, b.ReviewAppraiser, b.ReviewType, b.AppraisalReceivedDate, b.ApprovedJustCompensation, b.ReviewCompletedDate, b.ReviewSubmittedDate,
            b.ReviewRecommendation, b.ReviewRecommendedValue, b.SubmittedToJatDate, b.ApprovedByJatDate, b.FtaConcurrenceNeeded), ct));

    /// <summary>Changes an appraisal. Only the Appraisal Approver (and Admin) may change the review fields; anyone else may send them back unchanged.</summary>
    [HttpPut("appraisals/{appraisalId:guid}")]
    [ProducesResponseType(typeof(AppraisalDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAppraisal(Guid parcelId, Guid appraisalId, [FromBody] AppraisalBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateAppraisalCommand(
            parcelId, appraisalId, b.AppraisalType, b.Appraiser, b.LandPlannerRequired, b.AssignedTo, b.RequestedDate, b.ReceivedDate, b.ReportDate, b.DueDate, b.SubmittedToAtpDate,
            b.EffectiveDate, b.Amounts, b.ReviewAppraiser, b.ReviewType, b.AppraisalReceivedDate, b.ApprovedJustCompensation, b.ReviewCompletedDate, b.ReviewSubmittedDate,
            b.ReviewRecommendation, b.ReviewRecommendedValue, b.SubmittedToJatDate, b.ApprovedByJatDate, b.FtaConcurrenceNeeded), ct));

    [HttpDelete("appraisals/{appraisalId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveAppraisal(Guid parcelId, Guid appraisalId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveAppraisalCommand(parcelId, appraisalId), ct);
        return NoContent();
    }

    [HttpGet("questionnaires")]
    [ProducesResponseType(typeof(IReadOnlyList<QuestionnaireDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Questionnaires(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListQuestionnairesQuery(parcelId), ct));

    [HttpPost("questionnaires")]
    [ProducesResponseType(typeof(QuestionnaireDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddQuestionnaire(Guid parcelId, [FromBody] QuestionnaireBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/questionnaires", await _mediator.Send(new AddQuestionnaireCommand(parcelId, b.RelocationType ?? "", b.DueByDate, b.SentToOwnerDate, b.Interests, b.Questions), ct));

    [HttpPut("questionnaires/{questionnaireId:guid}")]
    [ProducesResponseType(typeof(QuestionnaireDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateQuestionnaire(Guid parcelId, Guid questionnaireId, [FromBody] QuestionnaireBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateQuestionnaireCommand(parcelId, questionnaireId, b.RelocationType ?? "", b.DueByDate, b.SentToOwnerDate, b.Interests, b.Questions), ct));

    [HttpDelete("questionnaires/{questionnaireId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveQuestionnaire(Guid parcelId, Guid questionnaireId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveQuestionnaireCommand(parcelId, questionnaireId), ct);
        return NoContent();
    }

    [HttpGet("condemnation")]
    [ProducesResponseType(typeof(CondemnationEnvelope), StatusCodes.Status200OK)]
    public async Task<IActionResult> Condemnation(Guid parcelId, CancellationToken ct) => Ok(new CondemnationEnvelope(await _mediator.Send(new GetCondemnationQuery(parcelId), ct)));

    /// <summary>Saves the parcel's one condemnation record: the first save creates it (needs Create), later saves change it (need Update). The grids are replaced as sent.</summary>
    [HttpPut("condemnation")]
    [ProducesResponseType(typeof(CondemnationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveCondemnation(Guid parcelId, [FromBody] CondemnationBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SaveCondemnationCommand(parcelId, b.Details!, b.StatusNotes, b.Attorneys, b.Commissioners, b.Notices), ct));

    [HttpDelete("condemnation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveCondemnation(Guid parcelId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveCondemnationCommand(parcelId), ct);
        return NoContent();
    }

    [HttpGet("property-management")]
    [ProducesResponseType(typeof(PropertyEnvelope), StatusCodes.Status200OK)]
    public async Task<IActionResult> Property(Guid parcelId, CancellationToken ct) => Ok(new PropertyEnvelope(await _mediator.Send(new GetPropertyQuery(parcelId), ct)));

    /// <summary>Saves the parcel's one property management and disposition record (first save needs Create, later saves Update). The funding grid is replaced as sent.</summary>
    [HttpPut("property-management")]
    [ProducesResponseType(typeof(PropertyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveProperty(Guid parcelId, [FromBody] PropertyBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SavePropertyCommand(parcelId, b.Details!, b.StatusNotes, b.Funding), ct));

    [HttpDelete("property-management")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveProperty(Guid parcelId, CancellationToken ct)
    {
        await _mediator.Send(new RemovePropertyCommand(parcelId), ct);
        return NoContent();
    }

    [HttpGet("relocations")]
    [ProducesResponseType(typeof(IReadOnlyList<RelocationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Relocations(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListRelocationsQuery(parcelId), ct));

    [HttpPost("relocations")]
    [ProducesResponseType(typeof(RelocationDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddRelocation(Guid parcelId, [FromBody] RelocationBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/relocations", await _mediator.Send(new AddRelocationCommand(parcelId, b.Details!, b.StatusNotes, b.Agents, b.Contacts, b.Eligibilities, b.ContactLog), ct));

    [HttpPut("relocations/{relocationId:guid}")]
    [ProducesResponseType(typeof(RelocationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateRelocation(Guid parcelId, Guid relocationId, [FromBody] RelocationBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateRelocationCommand(parcelId, relocationId, b.Details!, b.StatusNotes, b.Agents, b.Contacts, b.Eligibilities, b.ContactLog), ct));

    [HttpDelete("relocations/{relocationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveRelocation(Guid parcelId, Guid relocationId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveRelocationCommand(parcelId, relocationId), ct);
        return NoContent();
    }

    [HttpGet("negotiation")]
    [ProducesResponseType(typeof(NegotiationEnvelope), StatusCodes.Status200OK)]
    public async Task<IActionResult> Negotiation(Guid parcelId, CancellationToken ct) => Ok(new NegotiationEnvelope(await _mediator.Send(new GetNegotiationQuery(parcelId), ct)));

    /// <summary>Saves the parcel's one negotiation record: the first save creates it (needs Create), later saves change it (need Update). The grids are replaced as sent.</summary>
    [HttpPut("negotiation")]
    [ProducesResponseType(typeof(NegotiationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveNegotiation(Guid parcelId, [FromBody] NegotiationBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SaveNegotiationCommand(
            parcelId, b.AssignedTo, b.TotalAgreementAmount, b.Notes, b.CounterofferSubmittedToJatDate, b.CounterofferResponseFromJatDate, b.JatResponse,
            b.FtaApprovalRequiredForCounteroffer, b.PuaRequestedDate, b.PuaApprovedDate, b.PuaApprovedAmount, b.PuaExecutedDate, b.PuaClosedDate,
            b.StatusNotes, b.Agents, b.RightsOfEntry, b.Offers, b.Settlements), ct));

    [HttpDelete("negotiation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveNegotiation(Guid parcelId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveNegotiationCommand(parcelId), ct);
        return NoContent();
    }

    [HttpGet("title-closing")]
    [ProducesResponseType(typeof(TitleClosingEnvelope), StatusCodes.Status200OK)]
    public async Task<IActionResult> TitleClosing(Guid parcelId, CancellationToken ct) => Ok(new TitleClosingEnvelope(await _mediator.Send(new GetTitleClosingQuery(parcelId), ct)));

    /// <summary>Saves the record: the first save creates it (needs Create), later saves change it (need Update).</summary>
    [HttpPut("title-closing")]
    [ProducesResponseType(typeof(TitleClosingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveTitleClosing(Guid parcelId, [FromBody] TitleClosingBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SaveTitleClosingCommand(
            parcelId, b.AssignedTo, b.TitleCompany, b.TitleClosingAgent, b.GfNumber, b.FinalTitlePolicyNo, b.PsaSignedByOwnerDate, b.PsaSignedByAtpDate, b.PsaAmount,
            b.SettlementReceivedDate, b.SettlementExecutedDate, b.CurativeItemsCompleted, b.ClosingPackageSubmittedDate, b.ClosingPackageApprovedDate,
            b.ClosingInstructionDate, b.AnticipatedClosingDate, b.ActualClosingDate, b.ConveyanceDocuments, b.TitleCommitments), ct));

    [HttpDelete("title-closing")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveTitleClosing(Guid parcelId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveTitleClosingCommand(parcelId), ct);
        return NoContent();
    }

    [HttpGet("encumbrances")]
    [ProducesResponseType(typeof(IReadOnlyList<EncumbranceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Encumbrances(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListEncumbrancesQuery(parcelId), ct));

    [HttpPost("encumbrances")]
    [ProducesResponseType(typeof(EncumbranceDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddEncumbrance(Guid parcelId, [FromBody] EncumbranceBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/encumbrances", await _mediator.Send(new AddEncumbranceCommand(
            parcelId, b.EncumbranceType ?? "", b.DocumentTitle ?? "", b.DocumentDate, b.DateRecorded, b.DocumentNumber, b.IsUnrecorded, b.Grantor ?? "", b.Grantee ?? "",
            b.ExceptionSubmittedDate, b.ExceptionApprovedDate, b.ImpactAnalysis, b.RecommendedCure, b.CureStatus, b.ReviewNotes, b.Links), ct));

    [HttpPut("encumbrances/{encumbranceId:guid}")]
    [ProducesResponseType(typeof(EncumbranceDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEncumbrance(Guid parcelId, Guid encumbranceId, [FromBody] EncumbranceBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateEncumbranceCommand(
            parcelId, encumbranceId, b.EncumbranceType ?? "", b.DocumentTitle ?? "", b.DocumentDate, b.DateRecorded, b.DocumentNumber, b.IsUnrecorded, b.Grantor ?? "", b.Grantee ?? "",
            b.ExceptionSubmittedDate, b.ExceptionApprovedDate, b.ImpactAnalysis, b.RecommendedCure, b.CureStatus, b.ReviewNotes, b.Links), ct));

    [HttpDelete("encumbrances/{encumbranceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveEncumbrance(Guid parcelId, Guid encumbranceId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveEncumbranceCommand(parcelId, encumbranceId), ct);
        return NoContent();
    }

    [HttpGet("easements")]
    [ProducesResponseType(typeof(IReadOnlyList<EasementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Easements(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListEasementsQuery(parcelId), ct));

    [HttpPost("easements")]
    [ProducesResponseType(typeof(EasementDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddEasement(Guid parcelId, [FromBody] EasementBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/easements", await _mediator.Send(new AddEasementCommand(parcelId, b.Number ?? "", b.EasementType, b.Description, b.StartDate, b.EndDate, b.StartStation, b.EndStation), ct));

    [HttpPut("easements/{easementId:guid}")]
    [ProducesResponseType(typeof(EasementDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEasement(Guid parcelId, Guid easementId, [FromBody] EasementBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateEasementCommand(parcelId, easementId, b.Number ?? "", b.EasementType, b.Description, b.StartDate, b.EndDate, b.StartStation, b.EndStation), ct));

    [HttpDelete("easements/{easementId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveEasement(Guid parcelId, Guid easementId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveEasementCommand(parcelId, easementId), ct);
        return NoContent();
    }
}
