using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.ParcelRecords;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.ParcelTabs;

// ── Ownership Details (Phase 1.5, ADR 0017) ─────────────────────────────

public sealed record OwnershipAddressInput(string? AddressType, string? Address, string? ContactPerson, string? City, string? State, string? Zip);
public sealed record OwnershipContactInput(string? ContactType, string Name, string? Phone, string? Email, string? Address, bool IsActive, bool IsPrimary);

public sealed record OwnershipAddressDto(Guid Id, string? AddressType, string? Address, string? ContactPerson, string? City, string? State, string? Zip);
public sealed record OwnershipContactDto(Guid Id, string? ContactType, string Name, string? Phone, string? Email, string? Address, bool IsActive, bool IsPrimary);

public sealed record OwnershipDto(
    Guid Id, Guid ParcelId, int OwnershipNumber, string ParcelNumber, string OwnerName, string? OwnerType,
    IReadOnlyList<OwnershipAddressDto> Addresses, IReadOnlyList<OwnershipContactDto> Contacts, DateTime UpdatedAt);

internal static class OwnershipRules
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    /// <summary>One adder per parcel at a time in this process; the unique (parcel, sequence) index backstops other instances.</summary>
    public static SemaphoreSlim Gate(Guid parcelId) => Gates.GetOrAdd(parcelId, _ => new SemaphoreSlim(1, 1));

    public static OwnershipDto ToDto(ParcelOwnership o, string parcelNumber) =>
        new(o.Id, o.ParcelId, o.Sequence, parcelNumber, o.OwnerName, o.OwnerType,
            o.Addresses.OrderBy(a => a.CreatedAt).ThenBy(a => a.Address).Select(a => new OwnershipAddressDto(a.Id, a.AddressType, a.Address, a.ContactPerson, a.City, a.State, a.Zip)).ToList(),
            o.Contacts.OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new OwnershipContactDto(c.Id, c.ContactType, c.Name, c.Phone, c.Email, c.Address, c.IsActive, c.IsPrimary)).ToList(),
            o.UpdatedAt);

    public static List<ParcelOwnershipAddress> BuildAddresses(IReadOnlyList<OwnershipAddressInput>? input) =>
        (input ?? Array.Empty<OwnershipAddressInput>()).Select(a => new ParcelOwnershipAddress
        {
            AddressType = ParcelRecordHelpers.Clean(a.AddressType), Address = ParcelRecordHelpers.Clean(a.Address), ContactPerson = ParcelRecordHelpers.Clean(a.ContactPerson),
            City = ParcelRecordHelpers.Clean(a.City), State = ParcelRecordHelpers.Clean(a.State), Zip = ParcelRecordHelpers.Clean(a.Zip),
        }).ToList();

    public static List<ParcelOwnershipContact> BuildContacts(IReadOnlyList<OwnershipContactInput>? input) =>
        (input ?? Array.Empty<OwnershipContactInput>()).Select(c => new ParcelOwnershipContact
        {
            ContactType = ParcelRecordHelpers.Clean(c.ContactType), Name = c.Name.Trim(), Phone = ParcelRecordHelpers.Clean(c.Phone), Email = ParcelRecordHelpers.Clean(c.Email),
            Address = ParcelRecordHelpers.Clean(c.Address), IsActive = c.IsActive, IsPrimary = c.IsPrimary,
        }).ToList();
}

public interface IOwnershipFields
{
    string OwnerName { get; }
    string? OwnerType { get; }
    IReadOnlyList<OwnershipAddressInput>? Addresses { get; }
    IReadOnlyList<OwnershipContactInput>? Contacts { get; }
}

public abstract class OwnershipFieldsValidator<T> : AbstractValidator<T> where T : IOwnershipFields
{
    protected OwnershipFieldsValidator()
    {
        RuleFor(x => x.OwnerName).NotEmpty().WithMessage("Owner name is required.").MaximumLength(200);
        RuleFor(x => x.OwnerType).MaximumLength(100);
        RuleFor(x => x.Addresses).Must(a => a is null || a.Count <= 50).WithMessage("An ownership can have at most 50 addresses.");
        RuleForEach(x => x.Addresses).ChildRules(a =>
        {
            a.RuleFor(i => i.AddressType).MaximumLength(100);
            a.RuleFor(i => i.Address).MaximumLength(300);
            a.RuleFor(i => i.ContactPerson).MaximumLength(200);
            a.RuleFor(i => i.City).MaximumLength(100);
            a.RuleFor(i => i.State).MaximumLength(50);
            a.RuleFor(i => i.Zip).MaximumLength(20);
        });
        RuleFor(x => x.Contacts).Must(c => c is null || c.Count <= 50).WithMessage("An ownership can have at most 50 contacts.");
        RuleFor(x => x.Contacts).Must(c => c is null || c.Count(i => i.IsPrimary) <= 1).WithMessage("Only one contact can be the primary contact.");
        RuleForEach(x => x.Contacts).ChildRules(c =>
        {
            c.RuleFor(i => i.Name).NotEmpty().WithMessage("Each contact needs a name.").MaximumLength(200);
            c.RuleFor(i => i.ContactType).MaximumLength(100);
            c.RuleFor(i => i.Phone).Matches(@"^[\d\s().+\-xX#]{3,40}$").WithMessage("Enter a valid phone number.").When(i => !string.IsNullOrWhiteSpace(i.Phone));
            c.RuleFor(i => i.Email).EmailAddress().WithMessage("Enter a valid email address.").MaximumLength(200).When(i => !string.IsNullOrWhiteSpace(i.Email));
            c.RuleFor(i => i.Address).MaximumLength(300);
        });
    }
}

public sealed record ListOwnershipsQuery(Guid ParcelId) : IRequest<IReadOnlyList<OwnershipDto>>;

public sealed class ListOwnershipsQueryHandler : IRequestHandler<ListOwnershipsQuery, IReadOnlyList<OwnershipDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public ListOwnershipsQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<IReadOnlyList<OwnershipDto>> Handle(ListOwnershipsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Ownership, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var rows = await _db.ParcelOwnerships.Include(o => o.Addresses).Include(o => o.Contacts).Where(o => o.ParcelId == parcel.Id).ToListAsync(ct);
        return rows.OrderBy(o => o.Sequence).Select(o => OwnershipRules.ToDto(o, parcel.Number)).ToList();
    }
}

public sealed record AddOwnershipCommand(
    Guid ParcelId, string OwnerName, string? OwnerType, IReadOnlyList<OwnershipAddressInput>? Addresses, IReadOnlyList<OwnershipContactInput>? Contacts)
    : IRequest<OwnershipDto>, IOwnershipFields;

public sealed class AddOwnershipCommandValidator : OwnershipFieldsValidator<AddOwnershipCommand>
{
    public AddOwnershipCommandValidator() => RuleFor(x => x.ParcelId).NotEmpty();
}

public sealed class AddOwnershipCommandHandler : IRequestHandler<AddOwnershipCommand, OwnershipDto>
{
    private const int MaxAttempts = 6;
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public AddOwnershipCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<OwnershipDto> Handle(AddOwnershipCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Ownership, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);

        var gate = OwnershipRules.Gate(parcel.Id);
        await gate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var sequence = (await _db.ParcelOwnerships.Where(o => o.ParcelId == parcel.Id).MaxAsync(o => (int?)o.Sequence, ct) ?? 0) + 1;
                var ownership = new ParcelOwnership
                {
                    ParcelId = parcel.Id, Sequence = sequence, OwnerName = request.OwnerName.Trim(), OwnerType = ParcelRecordHelpers.Clean(request.OwnerType),
                    Addresses = OwnershipRules.BuildAddresses(request.Addresses), Contacts = OwnershipRules.BuildContacts(request.Contacts),
                };
                _db.ParcelOwnerships.Add(ownership);
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    _db.ChangeTracker.Clear();
                    continue;
                }
                return OwnershipRules.ToDto(ownership, parcel.Number);
            }
        }
        finally
        {
            gate.Release();
        }
        throw new ConflictException("ownership_busy", "The system is busy numbering ownerships. Please try again.");
    }
}

public sealed record UpdateOwnershipCommand(
    Guid ParcelId, Guid OwnershipId, string OwnerName, string? OwnerType, IReadOnlyList<OwnershipAddressInput>? Addresses, IReadOnlyList<OwnershipContactInput>? Contacts)
    : IRequest<OwnershipDto>, IOwnershipFields;

public sealed class UpdateOwnershipCommandValidator : OwnershipFieldsValidator<UpdateOwnershipCommand>
{
    public UpdateOwnershipCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.OwnershipId).NotEmpty();
    }
}

public sealed class UpdateOwnershipCommandHandler : IRequestHandler<UpdateOwnershipCommand, OwnershipDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public UpdateOwnershipCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<OwnershipDto> Handle(UpdateOwnershipCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Ownership, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var ownership = await _db.ParcelOwnerships.Include(o => o.Addresses).Include(o => o.Contacts)
                            .FirstOrDefaultAsync(o => o.Id == request.OwnershipId && o.ParcelId == parcel.Id, ct)
                        ?? throw new NotFoundException("Ownership", request.OwnershipId);

        ownership.OwnerName = request.OwnerName.Trim();
        ownership.OwnerType = ParcelRecordHelpers.Clean(request.OwnerType);
        _db.ParcelOwnershipAddresses.RemoveRange(ownership.Addresses);
        _db.ParcelOwnershipContacts.RemoveRange(ownership.Contacts);
        var addresses = OwnershipRules.BuildAddresses(request.Addresses);
        var contacts = OwnershipRules.BuildContacts(request.Contacts);
        foreach (var a in addresses) a.OwnershipId = ownership.Id;
        foreach (var c in contacts) c.OwnershipId = ownership.Id;
        _db.ParcelOwnershipAddresses.AddRange(addresses);
        _db.ParcelOwnershipContacts.AddRange(contacts);
        await _db.SaveChangesAsync(ct);

        var fresh = await _db.ParcelOwnerships.Include(o => o.Addresses).Include(o => o.Contacts).AsNoTracking().FirstAsync(o => o.Id == ownership.Id, ct);
        return OwnershipRules.ToDto(fresh, parcel.Number);
    }
}

public sealed record RemoveOwnershipCommand(Guid ParcelId, Guid OwnershipId) : IRequest;

public sealed class RemoveOwnershipCommandHandler : IRequestHandler<RemoveOwnershipCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveOwnershipCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveOwnershipCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Ownership, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var ownership = await _db.ParcelOwnerships.Include(o => o.Addresses).Include(o => o.Contacts)
                            .FirstOrDefaultAsync(o => o.Id == request.OwnershipId && o.ParcelId == parcel.Id, ct)
                        ?? throw new NotFoundException("Ownership", request.OwnershipId);
        _db.ParcelOwnershipAddresses.RemoveRange(ownership.Addresses);
        _db.ParcelOwnershipContacts.RemoveRange(ownership.Contacts);
        _db.ParcelOwnerships.Remove(ownership);
        await _db.SaveChangesAsync(ct);
    }
}
