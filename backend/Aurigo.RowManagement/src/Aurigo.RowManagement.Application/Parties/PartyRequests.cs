using Aurigo.RowManagement.Application.Agency;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Application.Security;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Parties;

// ── List and find (ROW-014) ─────────────────────────────────────────────

public sealed record ListPartiesQuery(string? Search, PartyKind? Kind, int Page = 1, int PageSize = 25) : IRequest<PagedResult<PartySummaryDto>>;

public sealed class ListPartiesQueryValidator : AbstractValidator<ListPartiesQuery>
{
    public ListPartiesQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Kind).IsInEnum().When(x => x.Kind is not null);
    }
}

public sealed class ListPartiesQueryHandler : IRequestHandler<ListPartiesQuery, PagedResult<PartySummaryDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly PartyService _parties;

    public ListPartiesQueryHandler(IRowDbContext db, IPagePermissionService permissions, PartyService parties)
    {
        _db = db;
        _permissions = permissions;
        _parties = parties;
    }

    public async Task<PagedResult<PartySummaryDto>> Handle(ListPartiesQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Read, ct);
        var query = _db.Parties.AsQueryable();
        if (request.Kind is { } kind) query = query.Where(p => p.Kind == kind);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term)
                || (p.LegalName != null && p.LegalName.ToLower().Contains(term))
                || (p.City != null && p.City.ToLower().Contains(term))
                || (p.Email != null && p.Email.ToLower().Contains(term))
                || (p.Phone != null && p.Phone.Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(p => p.Name).ThenBy(p => p.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var counts = await _parties.LinkCountsAsync(rows.Select(r => r.Id).ToList(), ct);
        return new PagedResult<PartySummaryDto>(rows.Select(p => PartyService.ToSummary(p, counts.GetValueOrDefault(p.Id))).ToList(), total, request.Page, request.PageSize);
    }
}

public sealed record GetPartyQuery(Guid Id) : IRequest<PartyDto>;

public sealed class GetPartyQueryHandler : IRequestHandler<GetPartyQuery, PartyDto>
{
    private readonly IPagePermissionService _permissions;
    private readonly PartyService _parties;

    public GetPartyQueryHandler(IPagePermissionService permissions, PartyService parties)
    {
        _permissions = permissions;
        _parties = parties;
    }

    public async Task<PartyDto> Handle(GetPartyQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Read, ct);
        return await _parties.ToDtoAsync(await _parties.LoadPartyAsync(request.Id, ct), ct);
    }
}

/// <summary>Every tract and parcel a party appears on, and in what role.</summary>
public sealed record GetPartyUsageQuery(Guid Id) : IRequest<IReadOnlyList<PartyUsageDto>>;

public sealed class GetPartyUsageQueryHandler : IRequestHandler<GetPartyUsageQuery, IReadOnlyList<PartyUsageDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly PartyService _parties;

    public GetPartyUsageQueryHandler(IRowDbContext db, IPagePermissionService permissions, PartyService parties)
    {
        _db = db;
        _permissions = permissions;
        _parties = parties;
    }

    public async Task<IReadOnlyList<PartyUsageDto>> Handle(GetPartyUsageQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Read, ct);
        var party = await _parties.LoadPartyAsync(request.Id, ct);
        var links = await _db.PartyLinks.Where(l => l.PartyId == party.Id).ToListAsync(ct);

        var parcelIds = links.Where(l => l.ParcelId is not null).Select(l => l.ParcelId!.Value).ToList();
        var tractIds = links.Where(l => l.ParentTractId is not null).Select(l => l.ParentTractId!.Value).ToList();
        var parcels = await _db.Parcels.Where(p => parcelIds.Contains(p.Id)).Select(p => new { p.Id, p.Number, p.ProjectId }).ToDictionaryAsync(p => p.Id, ct);
        var tracts = await _db.ParentTracts.Where(t => tractIds.Contains(t.Id)).Select(t => new { t.Id, t.Number, t.ProjectId }).ToDictionaryAsync(t => t.Id, ct);
        var projectIds = parcels.Values.Select(p => p.ProjectId).Concat(tracts.Values.Select(t => t.ProjectId)).Distinct().ToList();
        var projects = await _db.Projects.Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, ct);

        var rows = new List<PartyUsageDto>();
        foreach (var l in links)
        {
            if (l.ParcelId is { } pid && parcels.TryGetValue(pid, out var parcel))
                rows.Add(new PartyUsageDto(l.Id, l.Role, PartyRoles.CategoryOf(l.Role), "Parcel", pid, $"Parcel {parcel.Number}", parcel.ProjectId, projects.GetValueOrDefault(parcel.ProjectId, "?")));
            else if (l.ParentTractId is { } tid && tracts.TryGetValue(tid, out var tract))
                rows.Add(new PartyUsageDto(l.Id, l.Role, PartyRoles.CategoryOf(l.Role), "Tract", tid, $"Tract {tract.Number}", tract.ProjectId, projects.GetValueOrDefault(tract.ProjectId, "?")));
        }
        return rows.OrderBy(r => r.ProjectNumber).ThenBy(r => r.TargetLabel).ThenBy(r => r.Role).ToList();
    }
}

// ── Create, edit, remove ────────────────────────────────────────────────

public sealed record CreatePartyCommand(
    PartyKind Kind, string Name, string? LegalName, string? Email, string? Phone, string? AddressLine1, string? AddressLine2,
    string? City, string? State, string? PostalCode, string? Notes, IReadOnlyList<PartyAddressInput>? Addresses = null, IReadOnlyList<PartyContactInput>? Contacts = null)
    : IRequest<PartyDto>, IPartyFields;

public sealed class CreatePartyCommandValidator : PartyFieldsValidator<CreatePartyCommand> { }

public sealed class CreatePartyCommandHandler : IRequestHandler<CreatePartyCommand, PartyDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public CreatePartyCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<PartyDto> Handle(CreatePartyCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Create, ct);
        var party = new Party();
        PartyRules.Apply(party, request);
        PartyRules.ApplyChildren(_db, party, request);
        _db.Parties.Add(party);
        await _db.SaveChangesAsync(ct);
        return PartyService.ToDto(party, 0);
    }
}

public sealed record UpdatePartyCommand(
    Guid Id, PartyKind Kind, string Name, string? LegalName, string? Email, string? Phone, string? AddressLine1, string? AddressLine2,
    string? City, string? State, string? PostalCode, string? Notes, IReadOnlyList<PartyAddressInput>? Addresses = null, IReadOnlyList<PartyContactInput>? Contacts = null)
    : IRequest<PartyDto>, IPartyFields;

public sealed class UpdatePartyCommandValidator : PartyFieldsValidator<UpdatePartyCommand>
{
    public UpdatePartyCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class UpdatePartyCommandHandler : IRequestHandler<UpdatePartyCommand, PartyDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly PartyService _parties;

    public UpdatePartyCommandHandler(IRowDbContext db, IPagePermissionService permissions, PartyService parties)
    {
        _db = db;
        _permissions = permissions;
        _parties = parties;
    }

    public async Task<PartyDto> Handle(UpdatePartyCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Update, ct);
        var party = await _parties.LoadPartyAsync(request.Id, ct);
        PartyRules.Apply(party, request);
        PartyRules.ApplyChildren(_db, party, request);
        await _db.SaveChangesAsync(ct);
        return await _parties.ToDtoAsync(party, ct);
    }
}

/// <summary>Removes a party that is not linked to anything. A party in use stays until it is unlinked.</summary>
public sealed record DeletePartyCommand(Guid Id) : IRequest;

public sealed class DeletePartyCommandHandler : IRequestHandler<DeletePartyCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly PartyService _parties;

    public DeletePartyCommandHandler(IRowDbContext db, IPagePermissionService permissions, PartyService parties)
    {
        _db = db;
        _permissions = permissions;
        _parties = parties;
    }

    public async Task Handle(DeletePartyCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Delete, ct);
        var party = await _parties.LoadPartyAsync(request.Id, ct);
        if (await _db.PartyLinks.AnyAsync(l => l.PartyId == party.Id, ct))
            throw new ConflictException("party_in_use", $"{party.Name} is still linked to a tract or parcel. Remove those links first.");
        _db.Parties.Remove(party);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Someone linked the party between the check and the delete; the foreign key refused it.
            _db.ChangeTracker.Clear();
            throw new ConflictException("party_in_use", $"{party.Name} is still linked to a tract or parcel. Remove those links first.");
        }
    }
}

// ── Tax information for payees (ROW-018) ────────────────────────────────

/// <summary>
/// Records a payee's tax details. The number is write-only: it is checked, encrypted and reduced to its last four digits,
/// and nothing returns it. Leave <see cref="TaxId"/> empty to change only the W-9 date or the 1099 flag.
/// </summary>
public sealed record SetPartyTaxInfoCommand(Guid PartyId, TaxIdType? Type, string? TaxId, DateOnly? W9ReceivedOn, bool Reportable1099) : IRequest<PartyDto>
{
    // A record prints every member; this one must never print the number (it would reach logs and exception text).
    public override string ToString() => $"SetPartyTaxInfoCommand {{ PartyId = {PartyId}, Type = {Type}, TaxId = [redacted], Reportable1099 = {Reportable1099} }}";
}

public sealed class SetPartyTaxInfoCommandValidator : AbstractValidator<SetPartyTaxInfoCommand>
{
    public SetPartyTaxInfoCommandValidator()
    {
        RuleFor(x => x.PartyId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum().When(x => x.Type is not null);
        RuleFor(x => x.Type).NotNull().WithMessage("Choose the type of tax number.").When(x => !string.IsNullOrWhiteSpace(x.TaxId));
        RuleFor(x => x.TaxId).MaximumLength(20).WithMessage("A tax number has 9 digits.");
        RuleFor(x => x.TaxId).Custom((value, ctx) =>
        {
            if (string.IsNullOrWhiteSpace(value) || ctx.InstanceToValidate.Type is not { } type) return;
            foreach (var problem in TaxId.Validate(type, value)) ctx.AddFailure(problem);
        });
        RuleFor(x => x.W9ReceivedOn).Must(d => d is null || (d.Value.Year >= 1990 && d.Value.Year <= 2100)).WithMessage("Enter a valid date.");
    }
}

public sealed class SetPartyTaxInfoCommandHandler : IRequestHandler<SetPartyTaxInfoCommand, PartyDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly PartyService _parties;
    private readonly ITaxIdProtector _protector;
    private readonly AgencyClock _clock;

    public SetPartyTaxInfoCommandHandler(IRowDbContext db, IPagePermissionService permissions, PartyService parties, ITaxIdProtector protector, AgencyClock clock)
    {
        _db = db;
        _permissions = permissions;
        _parties = parties;
        _protector = protector;
        _clock = clock;
    }

    public async Task<PartyDto> Handle(SetPartyTaxInfoCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Update, ct);
        var party = await _parties.LoadPartyAsync(request.PartyId, ct);

        if (request.W9ReceivedOn is { } received && received > AgencyTime.TodayIn(await _clock.AgencyZoneAsync(ct), _clock.UtcNow))
            throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("W9ReceivedOn", "A W-9 cannot be received in the future.") });

        if (!string.IsNullOrWhiteSpace(request.TaxId))
        {
            var digits = TaxId.Digits(request.TaxId);
            party.TaxIdType = request.Type;
            party.TaxIdProtected = _protector.Protect(digits, party.Id);
            party.TaxIdLast4 = TaxId.Last4(digits);
        }
        else if (request.Type is { } type && party.TaxIdType is { } current && type != current)
        {
            throw new InvalidRequestException("tax_id_type_change", "Enter the number again when changing its type.");
        }

        party.W9ReceivedOn = request.W9ReceivedOn;
        party.Reportable1099 = request.Reportable1099;
        await _db.SaveChangesAsync(ct);
        return await _parties.ToDtoAsync(party, ct);
    }
}

/// <summary>Removes the stored tax number (and its type) from a party. The W-9 date and the 1099 flag stay.</summary>
public sealed record ClearPartyTaxIdCommand(Guid PartyId) : IRequest<PartyDto>;

public sealed class ClearPartyTaxIdCommandHandler : IRequestHandler<ClearPartyTaxIdCommand, PartyDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly PartyService _parties;

    public ClearPartyTaxIdCommandHandler(IRowDbContext db, IPagePermissionService permissions, PartyService parties)
    {
        _db = db;
        _permissions = permissions;
        _parties = parties;
    }

    public async Task<PartyDto> Handle(ClearPartyTaxIdCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Update, ct);
        var party = await _parties.LoadPartyAsync(request.PartyId, ct);
        party.TaxIdType = null;
        party.TaxIdProtected = null;
        party.TaxIdLast4 = null;
        await _db.SaveChangesAsync(ct);
        return await _parties.ToDtoAsync(party, ct);
    }
}
