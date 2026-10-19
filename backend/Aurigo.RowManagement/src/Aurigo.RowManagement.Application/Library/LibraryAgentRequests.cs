using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Library;

// The Library's Agents list (Phase 1.5 slice 3, ADR 0015): the people the agency assigns to parcels, entered once. Everyone reads it
// (forms pick from it); the Library page permission governs changes. Agents are switched off, never deleted.

public sealed record LibraryAgentDto(Guid Id, string Name, string AgentType, string? Company, string? Phone, string? Email, bool IsActive);

internal static class LibraryAgentRules
{
    public static string KeyOf(string name, string type, string? company) =>
        $"{name.Trim().ToUpperInvariant()}|{type.Trim().ToUpperInvariant()}|{(company ?? string.Empty).Trim().ToUpperInvariant()}";

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static LibraryAgentDto ToDto(LibraryAgent a) => new(a.Id, a.Name, a.AgentType, a.Company, a.Phone, a.Email, a.IsActive);

    /// <summary>The job must be a value of the Agent type list (so reports group by the agency's own job names). Returns its stored spelling.</summary>
    public static async Task<string> ResolveTypeAsync(IRowDbContext db, string type, bool mustBeActive, CancellationToken ct)
    {
        var field = await db.LibraryFields.FirstOrDefaultAsync(f => f.Key == LibraryKeys.AgentType, ct)
            ?? throw new BusinessRuleException("The Agent type list is not set up.");
        var wanted = type.Trim().ToUpperInvariant();
        var value = await db.LibraryValues.FirstOrDefaultAsync(v => v.LibraryFieldId == field.Id && v.NormalizedValue == wanted, ct)
            ?? throw new InvalidRequestException("unknown_agent_type", $"'{type.Trim()}' is not in the Agent type list. Add it to the Library first.");
        if (mustBeActive && !value.IsActive)
            throw new InvalidRequestException("agent_type_inactive", $"'{value.Value}' is switched off in the Agent type list.");
        return value.Value;
    }
}

public sealed record ListLibraryAgentsQuery(string? AgentType, bool ActiveOnly) : IRequest<IReadOnlyList<LibraryAgentDto>>;

public sealed class ListLibraryAgentsQueryHandler : IRequestHandler<ListLibraryAgentsQuery, IReadOnlyList<LibraryAgentDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    public ListLibraryAgentsQueryHandler(IRowDbContext db, IPagePermissionService permissions) { _db = db; _permissions = permissions; }

    public async Task<IReadOnlyList<LibraryAgentDto>> Handle(ListLibraryAgentsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Library, PermissionAction.Read, ct);
        var query = _db.LibraryAgents.AsQueryable();
        if (request.ActiveOnly) query = query.Where(a => a.IsActive);
        if (!string.IsNullOrWhiteSpace(request.AgentType))
        {
            var type = request.AgentType.Trim().ToLower();
            query = query.Where(a => a.AgentType.ToLower() == type);
        }
        return (await query.OrderBy(a => a.Name).ThenBy(a => a.AgentType).ToListAsync(ct)).Select(LibraryAgentRules.ToDto).ToList();
    }
}

public sealed record SaveLibraryAgentCommand(Guid? Id, string Name, string AgentType, string? Company, string? Phone, string? Email, bool IsActive) : IRequest<LibraryAgentDto>;

public sealed class SaveLibraryAgentCommandValidator : AbstractValidator<SaveLibraryAgentCommand>
{
    public SaveLibraryAgentCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Agent name is required.").MaximumLength(150);
        RuleFor(x => x.AgentType).NotEmpty().WithMessage("Agent type is required.").MaximumLength(100);
        RuleFor(x => x.Company).MaximumLength(150);
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Email).MaximumLength(254).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("Enter a valid email address.");
    }
}

public sealed class SaveLibraryAgentCommandHandler : IRequestHandler<SaveLibraryAgentCommand, LibraryAgentDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentTenant _tenant;
    public SaveLibraryAgentCommandHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentTenant tenant) { _db = db; _permissions = permissions; _tenant = tenant; }

    public async Task<LibraryAgentDto> Handle(SaveLibraryAgentCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Library, request.Id is null ? PermissionAction.Create : PermissionAction.Update, ct);
        LibraryAgent? agent = null;
        if (request.Id is { } id)
            agent = await _db.LibraryAgents.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Agent", id);

        // A new or changed job must be an active value of the Agent type list; an unchanged one may have been switched off since.
        var changedType = agent is null || !string.Equals(agent.AgentType, request.AgentType.Trim(), StringComparison.OrdinalIgnoreCase);
        var type = await LibraryAgentRules.ResolveTypeAsync(_db, request.AgentType, mustBeActive: changedType, ct);
        var name = request.Name.Trim();
        var company = LibraryAgentRules.Clean(request.Company);
        var key = LibraryAgentRules.KeyOf(name, type, company);
        if (await _db.LibraryAgents.AnyAsync(a => a.NormalizedKey == key && (agent == null || a.Id != agent.Id), ct))
            throw new ConflictException("library_agent_exists", $"'{name}' is already listed as {type}{(company is null ? "" : " at " + company)}.");

        if (agent is null)
        {
            agent = new LibraryAgent { TenantId = _tenant.Value };
            _db.LibraryAgents.Add(agent);
        }
        agent.Name = name;
        agent.AgentType = type;
        agent.Company = company;
        agent.Phone = LibraryAgentRules.Clean(request.Phone);
        agent.Email = LibraryAgentRules.Clean(request.Email)?.ToLowerInvariant();
        agent.IsActive = request.IsActive;
        agent.NormalizedKey = key;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("library_agent_exists", $"'{name}' is already listed as {type}{(company is null ? "" : " at " + company)}.");
        }
        return LibraryAgentRules.ToDto(agent);
    }
}
