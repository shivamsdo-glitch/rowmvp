using Aurigo.RowManagement.Application.ParcelRecords;
using Aurigo.RowManagement.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

public sealed record TeamMemberBody(Guid AgentId, string? Notes);
public sealed record DiaryEntryBody(DateOnly OccurredOn, DiaryMethod Method, DiaryDirection Direction, string? ContactName, Guid? PartyId, string Summary, string? Outcome);
public sealed record NoteBody(string Text);

/// <summary>A parcel's team, negotiation diary and notes (ROW-019, 020, 021).</summary>
[ApiController]
[Authorize]
[Route("api/v1/parcels/{parcelId:guid}")]
public sealed class ParcelRecordsController : ControllerBase
{
    private readonly IMediator _mediator;
    public ParcelRecordsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("team")]
    [ProducesResponseType(typeof(IReadOnlyList<ParcelTeamMemberDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Team(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListParcelTeamQuery(parcelId), ct));

    [HttpPost("team")]
    [ProducesResponseType(typeof(ParcelTeamMemberDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddTeam(Guid parcelId, [FromBody] TeamMemberBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/team", await _mediator.Send(new AddTeamMemberCommand(parcelId, b.AgentId, b.Notes), ct));

    [HttpPut("team/{memberId:guid}")]
    [ProducesResponseType(typeof(ParcelTeamMemberDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTeam(Guid parcelId, Guid memberId, [FromBody] TeamMemberBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateTeamMemberCommand(parcelId, memberId, b.AgentId, b.Notes), ct));

    [HttpDelete("team/{memberId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveTeam(Guid parcelId, Guid memberId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveTeamMemberCommand(parcelId, memberId), ct);
        return NoContent();
    }

    [HttpGet("diary")]
    [ProducesResponseType(typeof(IReadOnlyList<DiaryEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Diary(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListParcelDiaryQuery(parcelId), ct));

    [HttpPost("diary")]
    [ProducesResponseType(typeof(DiaryEntryDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddDiary(Guid parcelId, [FromBody] DiaryEntryBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/diary", await _mediator.Send(new AddDiaryEntryCommand(parcelId, b.OccurredOn, b.Method, b.Direction, b.ContactName, b.PartyId, b.Summary, b.Outcome), ct));

    [HttpDelete("diary/{entryId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveDiary(Guid parcelId, Guid entryId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveDiaryEntryCommand(parcelId, entryId), ct);
        return NoContent();
    }

    [HttpGet("notes")]
    [ProducesResponseType(typeof(IReadOnlyList<ParcelNoteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Notes(Guid parcelId, CancellationToken ct) => Ok(await _mediator.Send(new ListParcelNotesQuery(parcelId), ct));

    [HttpPost("notes")]
    [ProducesResponseType(typeof(ParcelNoteDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddNote(Guid parcelId, [FromBody] NoteBody b, CancellationToken ct) =>
        Created($"/api/v1/parcels/{parcelId}/notes", await _mediator.Send(new AddParcelNoteCommand(parcelId, b.Text), ct));

    [HttpDelete("notes/{noteId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveNote(Guid parcelId, Guid noteId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveParcelNoteCommand(parcelId, noteId), ct);
        return NoContent();
    }
}
