using Aurigo.RowManagement.Application.SavedFilters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aurigo.RowManagement.Api.Controllers;

public sealed record SavedFilterBody(string Name, string FilterJson);

/// <summary>A user's saved list filters. Always scoped to the calling user; there is no way to see another user's.</summary>
[ApiController]
[Authorize]
[Route("api/v1/saved-filters")]
public sealed class SavedFiltersController : ControllerBase
{
    private readonly IMediator _mediator;
    public SavedFiltersController(IMediator mediator) => _mediator = mediator;

    /// <summary>The caller's saved filters for a page, by name.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SavedFilterDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string pageKey, CancellationToken ct) =>
        Ok(await _mediator.Send(new ListSavedFiltersQuery(pageKey), ct));

    [HttpPost]
    [ProducesResponseType(typeof(SavedFilterDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateSavedFilterCommand command, CancellationToken ct)
    {
        var created = await _mediator.Send(command, ct);
        return Created($"/api/v1/saved-filters?pageKey={created.PageKey}", created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SavedFilterDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SavedFilterBody body, CancellationToken ct) =>
        Ok(await _mediator.Send(new UpdateSavedFilterCommand(id, body.Name, body.FilterJson), ct));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteSavedFilterCommand(id), ct);
        return NoContent();
    }
}
