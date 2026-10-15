using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Parties;

/// <summary>A party entered while linking: the same details as a party made on its own (ROW-014).</summary>
public sealed record NewPartyFields(
    PartyKind Kind, string Name, string? LegalName, string? Email, string? Phone, string? AddressLine1, string? AddressLine2,
    string? City, string? State, string? PostalCode, string? Notes) : IPartyFields;

public sealed class NewPartyFieldsValidator : PartyFieldsValidator<NewPartyFields> { }

/// <summary>
/// How to link a party to a tract or parcel: an existing party (<see cref="PartyId"/>) or a new one (<see cref="NewParty"/>), the
/// role, and the role's details: a share for ownership interests, lease and insurance details for tenants.
/// </summary>
public sealed record PartyLinkInput(
    Guid? PartyId, NewPartyFields? NewParty, PartyRole Role, decimal? SharePercent, string? Notes,
    bool LeaseOnFile, string? LeaseDetails, string? InsuranceDetails);

public sealed class PartyLinkInputValidator : AbstractValidator<PartyLinkInput>
{
    public PartyLinkInputValidator()
    {
        RuleFor(x => x).Must(x => (x.PartyId is not null) ^ (x.NewParty is not null)).OverridePropertyName("Party")
            .WithMessage("Choose an existing party or enter a new one.");
        RuleFor(x => x.PartyId).NotEqual(Guid.Empty).When(x => x.PartyId is not null);
        RuleFor(x => x.NewParty!).SetValidator(new NewPartyFieldsValidator()).When(x => x.NewParty is not null);
        RuleFor(x => x.Role).IsInEnum();
        When(x => Enum.IsDefined(x.Role), () =>
        {
            RuleFor(x => x.SharePercent).Custom((share, ctx) =>
            {
                var input = ctx.InstanceToValidate;
                if (share is not null && !PartyRoles.CarriesShare(input.Role))
                    ctx.AddFailure("Only fee owners, easement holders and life-estate holders have a share.");
                else
                    foreach (var problem in OwnershipShares.CheckShare(share)) ctx.AddFailure(problem);
            });
            RuleFor(x => x).Must(x => PartyRoles.HasLease(x.Role) || (!x.LeaseOnFile && x.LeaseDetails is null && x.InsuranceDetails is null))
                .OverridePropertyName("LeaseDetails").WithMessage("Lease and insurance details apply to tenants only.");
        });
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.LeaseDetails).MaximumLength(1000);
        RuleFor(x => x.InsuranceDetails).MaximumLength(1000);
    }
}

internal static class PartyLinkWriter
{
    /// <summary>Links a party (existing or new) to a tract or parcel, applying the ownership rules, and saves.</summary>
    public static async Task<PartyLinkDto> AddAsync(
        IRowDbContext db, PartyService parties, PartyLinkInput input, Parcel? parcel, ParentTract? tract, CancellationToken ct)
    {
        var category = PartyRoles.CategoryOf(input.Role);

        // Owners belong to the tract of record. A parcel cut from a tract shows its tract's owners, so they are not entered twice.
        if (category == PartyCategory.Ownership && parcel?.ParentTractId is { } parentTractId)
        {
            var number = await db.ParentTracts.Where(t => t.Id == parentTractId).Select(t => t.Number).FirstOrDefaultAsync(ct);
            throw new InvalidRequestException("owners_on_tract", $"This parcel is part of tract {number}. Record its owners and interests on the tract.");
        }

        Party party;
        if (input.PartyId is { } partyId) party = await parties.LoadPartyAsync(partyId, ct);
        else
        {
            party = new Party();
            PartyRules.Apply(party, input.NewParty!);
            db.Parties.Add(party);
        }

        var targetLinks = db.PartyLinks.Where(l => parcel != null ? l.ParcelId == parcel.Id : l.ParentTractId == tract!.Id);
        if (input.PartyId is not null && await targetLinks.AnyAsync(l => l.PartyId == party.Id && l.Role == input.Role, ct))
            throw new ConflictException("party_link_exists", $"{party.Name} is already recorded here as {Describe(input.Role)}.");

        if (input.SharePercent is not null)
            await EnsureSharesFitAsync(targetLinks, input.Role, excludeLinkId: null, input.SharePercent, ct);

        var link = new PartyLink
        {
            PartyId = party.Id, ParcelId = parcel?.Id, ParentTractId = tract?.Id, Role = input.Role, SharePercent = input.SharePercent,
            Notes = PartyRules.Clean(input.Notes), LeaseOnFile = input.LeaseOnFile,
            LeaseDetails = PartyRules.Clean(input.LeaseDetails), InsuranceDetails = PartyRules.Clean(input.InsuranceDetails),
        };
        db.PartyLinks.Add(link);
        if (PartyRoles.CarriesShare(input.Role)) Touch(parcel, tract);
        await SaveAsync(db, ct);
        return (await parties.ToLinkDtosAsync(new[] { link }, ct)).Single();
    }

    public static async Task EnsureSharesFitAsync(IQueryable<PartyLink> targetLinks, PartyRole role, Guid? excludeLinkId, decimal? share, CancellationToken ct)
    {
        var others = await targetLinks.Where(l => l.Role == role && l.SharePercent != null && l.Id != excludeLinkId).Select(l => l.SharePercent).ToListAsync(ct);
        if (OwnershipShares.Exceeds(others, share))
            throw new ConflictException("share_exceeds_whole",
                $"The shares of {Describe(role, plural: true)} would add up to more than 100 percent. {OwnershipShares.Remaining(others):0.####} percent is unclaimed.");
    }

    /// <summary>
    /// Marks the parcel or tract as changed so two people adding shares at the same moment cannot both succeed (the row's
    /// concurrency token changes), which keeps the shares' total honest across servers.
    /// </summary>
    public static void Touch(Parcel? parcel, ParentTract? tract)
    {
        var now = DateTime.UtcNow;
        if (parcel is not null) parcel.UpdatedAt = now;
        if (tract is not null) tract.UpdatedAt = now;
    }

    public static async Task SaveAsync(IRowDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new ConflictException("party_changed", "Someone else changed the owners of this parcel at the same moment. Please open it again and retry.");
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            throw new ConflictException("party_link_exists", "That party is already recorded here in that role.");
        }
    }

    public static string Describe(PartyRole role, bool plural = false) => role switch
    {
        PartyRole.FeeOwner => plural ? "fee owners" : "a fee owner",
        PartyRole.EasementHolder => plural ? "easement holders" : "an easement holder",
        PartyRole.Lienholder => plural ? "lienholders" : "a lienholder",
        PartyRole.Trustee => plural ? "trustees" : "a trustee",
        PartyRole.LifeEstateHolder => plural ? "life-estate holders" : "a life-estate holder",
        PartyRole.OwnerDesignee => plural ? "owner designees" : "an owner designee",
        PartyRole.OwnerRepresentative => plural ? "owner representatives" : "an owner representative",
        _ => plural ? $"{role} parties" : $"a {role}",
    };
}

// ── The people around a parcel or tract ─────────────────────────────────

/// <summary>Everyone around a parcel: its own links and its parent tract's (ROW-014..017).</summary>
public sealed record ListParcelPartiesQuery(Guid ParcelId) : IRequest<ParcelPartiesDto>;

public sealed class ListParcelPartiesQueryHandler : IRequestHandler<ListParcelPartiesQuery, ParcelPartiesDto>
{
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly PartyService _parties;

    public ListParcelPartiesQueryHandler(IPagePermissionService permissions, ParcelService parcels, PartyService parties)
    {
        _permissions = permissions;
        _parcels = parcels;
        _parties = parties;
    }

    public async Task<ParcelPartiesDto> Handle(ListParcelPartiesQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Read, ct);
        return await _parties.ForParcelAsync(await _parcels.LoadAsync(request.ParcelId, ct), ct);
    }
}

public sealed record ListTractPartiesQuery(Guid ProjectId, Guid TractId) : IRequest<ParcelPartiesDto>;

public sealed class ListTractPartiesQueryHandler : IRequestHandler<ListTractPartiesQuery, ParcelPartiesDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly PartyService _parties;

    public ListTractPartiesQueryHandler(IRowDbContext db, IPagePermissionService permissions, PartyService parties)
    {
        _db = db;
        _permissions = permissions;
        _parties = parties;
    }

    public async Task<ParcelPartiesDto> Handle(ListTractPartiesQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Read, ct);
        var tract = await _db.ParentTracts.FirstOrDefaultAsync(t => t.Id == request.TractId && t.ProjectId == request.ProjectId, ct)
                    ?? throw new NotFoundException("Tract", request.TractId);
        return await _parties.ForTractAsync(tract, ct);
    }
}

// ── Adding ──────────────────────────────────────────────────────────────

public sealed record AddParcelPartyCommand(Guid ParcelId, PartyLinkInput Link) : IRequest<PartyLinkDto>;

public sealed class AddParcelPartyCommandValidator : AbstractValidator<AddParcelPartyCommand>
{
    public AddParcelPartyCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.Link).NotNull().SetValidator(new PartyLinkInputValidator());
    }
}

public sealed class AddParcelPartyCommandHandler : IRequestHandler<AddParcelPartyCommand, PartyLinkDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly PartyService _parties;

    public AddParcelPartyCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, PartyService parties)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _parties = parties;
    }

    public async Task<PartyLinkDto> Handle(AddParcelPartyCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        return await PartyLinkWriter.AddAsync(_db, _parties, request.Link, parcel, null, ct);
    }
}

public sealed record AddTractPartyCommand(Guid ProjectId, Guid TractId, PartyLinkInput Link) : IRequest<PartyLinkDto>;

public sealed class AddTractPartyCommandValidator : AbstractValidator<AddTractPartyCommand>
{
    public AddTractPartyCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.TractId).NotEmpty();
        RuleFor(x => x.Link).NotNull().SetValidator(new PartyLinkInputValidator());
    }
}

public sealed class AddTractPartyCommandHandler : IRequestHandler<AddTractPartyCommand, PartyLinkDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly PartyService _parties;

    public AddTractPartyCommandHandler(IRowDbContext db, IPagePermissionService permissions, PartyService parties)
    {
        _db = db;
        _permissions = permissions;
        _parties = parties;
    }

    public async Task<PartyLinkDto> Handle(AddTractPartyCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Create, ct);
        var tract = await _db.ParentTracts.FirstOrDefaultAsync(t => t.Id == request.TractId && t.ProjectId == request.ProjectId, ct)
                    ?? throw new NotFoundException("Tract", request.TractId);
        return await PartyLinkWriter.AddAsync(_db, _parties, request.Link, null, tract, ct);
    }
}

// ── Changing and removing ───────────────────────────────────────────────

/// <summary>Changes a link's share, notes or tenant details. Its party and role stay; to change those, remove it and add another.</summary>
public sealed record UpdatePartyLinkCommand(
    Guid LinkId, decimal? SharePercent, string? Notes, bool LeaseOnFile, string? LeaseDetails, string? InsuranceDetails) : IRequest<PartyLinkDto>;

public sealed class UpdatePartyLinkCommandValidator : AbstractValidator<UpdatePartyLinkCommand>
{
    public UpdatePartyLinkCommandValidator()
    {
        RuleFor(x => x.LinkId).NotEmpty();
        RuleFor(x => x.SharePercent).Custom((share, ctx) =>
        {
            foreach (var problem in OwnershipShares.CheckShare(share)) ctx.AddFailure(problem);
        });
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.LeaseDetails).MaximumLength(1000);
        RuleFor(x => x.InsuranceDetails).MaximumLength(1000);
    }
}

public sealed class UpdatePartyLinkCommandHandler : IRequestHandler<UpdatePartyLinkCommand, PartyLinkDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly PartyService _parties;

    public UpdatePartyLinkCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, PartyService parties)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _parties = parties;
    }

    public async Task<PartyLinkDto> Handle(UpdatePartyLinkCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Update, ct);
        var link = await _db.PartyLinks.FirstOrDefaultAsync(l => l.Id == request.LinkId, ct) ?? throw new NotFoundException("Party link", request.LinkId);
        var (parcel, tract) = await ResolveTargetAsync(_db, _parcels, link, ct);

        if (request.SharePercent is not null && !PartyRoles.CarriesShare(link.Role))
            throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("SharePercent", "Only fee owners, easement holders and life-estate holders have a share.") });
        if (!PartyRoles.HasLease(link.Role) && (request.LeaseOnFile || request.LeaseDetails is not null || request.InsuranceDetails is not null))
            throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure("LeaseDetails", "Lease and insurance details apply to tenants only.") });

        if (request.SharePercent is not null)
        {
            var targetLinks = _db.PartyLinks.Where(l => link.ParcelId != null ? l.ParcelId == link.ParcelId : l.ParentTractId == link.ParentTractId);
            await PartyLinkWriter.EnsureSharesFitAsync(targetLinks, link.Role, link.Id, request.SharePercent, ct);
        }

        link.SharePercent = request.SharePercent;
        link.Notes = PartyRules.Clean(request.Notes);
        link.LeaseOnFile = request.LeaseOnFile;
        link.LeaseDetails = PartyRules.Clean(request.LeaseDetails);
        link.InsuranceDetails = PartyRules.Clean(request.InsuranceDetails);
        if (PartyRoles.CarriesShare(link.Role)) PartyLinkWriter.Touch(parcel, tract);
        await PartyLinkWriter.SaveAsync(_db, ct);
        return (await _parties.ToLinkDtosAsync(new[] { link }, ct)).Single();
    }

    internal static async Task<(Parcel?, ParentTract?)> ResolveTargetAsync(IRowDbContext db, ParcelService parcels, PartyLink link, CancellationToken ct)
    {
        if (link.ParcelId is { } parcelId) return (await parcels.LoadActiveAsync(parcelId, ct), null);
        var tract = await db.ParentTracts.FirstOrDefaultAsync(t => t.Id == link.ParentTractId, ct) ?? throw new NotFoundException("Tract", link.ParentTractId!);
        return (null, tract);
    }
}

public sealed record DeletePartyLinkCommand(Guid LinkId) : IRequest;

public sealed class DeletePartyLinkCommandHandler : IRequestHandler<DeletePartyLinkCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public DeletePartyLinkCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(DeletePartyLinkCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parties, PermissionAction.Delete, ct);
        var link = await _db.PartyLinks.FirstOrDefaultAsync(l => l.Id == request.LinkId, ct) ?? throw new NotFoundException("Party link", request.LinkId);
        var (parcel, tract) = await UpdatePartyLinkCommandHandler.ResolveTargetAsync(_db, _parcels, link, ct);
        _db.PartyLinks.Remove(link);
        if (PartyRoles.CarriesShare(link.Role)) PartyLinkWriter.Touch(parcel, tract);
        await PartyLinkWriter.SaveAsync(_db, ct);
    }
}
