using Aurigo.RowManagement.Application.Library;
using Aurigo.RowManagement.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

public sealed record LibraryFieldBody(string Label, CustomFieldType FieldType);
public sealed record LibraryValueBody(string Value, bool IsActive = true);
public sealed record LibraryImportBody(List<string> Values);
public sealed record LibraryAgentBody(string Name, string AgentType, string? Company, string? Phone, string? Email, bool IsActive = true);

/// <summary>
/// The Library: fields the agency defines once (name, type, and for a list its allowed values) that forms then use. Default
/// fields and values are attached when an agency is set up. Controlled by the Administration page.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/library")]
public sealed class LibraryController : ControllerBase
{
    private readonly IMediator _mediator;
    public LibraryController(IMediator mediator) => _mediator = mediator;

    [HttpGet("fields")]
    [ProducesResponseType(typeof(IReadOnlyList<LibraryFieldSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _mediator.Send(new ListLibraryFieldsQuery(), ct));

    [HttpGet("fields/{id:guid}")]
    [ProducesResponseType(typeof(LibraryFieldDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await _mediator.Send(new GetLibraryFieldQuery(id), ct));

    [HttpPost("fields")]
    [ProducesResponseType(typeof(LibraryFieldDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] LibraryFieldBody b, CancellationToken ct)
    {
        var field = await _mediator.Send(new CreateLibraryFieldCommand(b.Label, b.FieldType), ct);
        return Created($"/api/v1/library/fields/{field.Id}", field);
    }

    [HttpPut("fields/{id:guid}")]
    [ProducesResponseType(typeof(LibraryFieldDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] LibraryFieldBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateLibraryFieldCommand(id, b.Label, b.FieldType), ct));

    [HttpPost("fields/{id:guid}/values")]
    [ProducesResponseType(typeof(LibraryFieldDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddValue(Guid id, [FromBody] LibraryValueBody b, CancellationToken ct) =>
        Created($"/api/v1/library/fields/{id}", await _mediator.Send(new AddLibraryValueCommand(id, b.Value), ct));

    [HttpPut("fields/{id:guid}/values/{valueId:guid}")]
    [ProducesResponseType(typeof(LibraryFieldDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateValue(Guid id, Guid valueId, [FromBody] LibraryValueBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateLibraryValueCommand(id, valueId, b.Value, b.IsActive), ct));

    [HttpPost("fields/{id:guid}/import")]
    [ProducesResponseType(typeof(LibraryImportResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Import(Guid id, [FromBody] LibraryImportBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new ImportLibraryValuesCommand(id, b.Values ?? new List<string>()), ct));

    /// <summary>The agents list. Everyone with Library access reads it (forms pick from it); optionally only one job, only active people.</summary>
    [HttpGet("agents")]
    [ProducesResponseType(typeof(IReadOnlyList<LibraryAgentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Agents([FromQuery] string? agentType, [FromQuery] bool activeOnly = false, CancellationToken ct = default) =>
        Ok(await _mediator.Send(new ListLibraryAgentsQuery(agentType, activeOnly), ct));

    [HttpPost("agents")]
    [ProducesResponseType(typeof(LibraryAgentDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAgent([FromBody] LibraryAgentBody b, CancellationToken ct)
    {
        var agent = await _mediator.Send(new SaveLibraryAgentCommand(null, b.Name, b.AgentType, b.Company, b.Phone, b.Email, b.IsActive), ct);
        return Created($"/api/v1/library/agents/{agent.Id}", agent);
    }

    [HttpPut("agents/{id:guid}")]
    [ProducesResponseType(typeof(LibraryAgentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateAgent(Guid id, [FromBody] LibraryAgentBody b, CancellationToken ct) =>
        Ok(await _mediator.Send(new SaveLibraryAgentCommand(id, b.Name, b.AgentType, b.Company, b.Phone, b.Email, b.IsActive), ct));
}
