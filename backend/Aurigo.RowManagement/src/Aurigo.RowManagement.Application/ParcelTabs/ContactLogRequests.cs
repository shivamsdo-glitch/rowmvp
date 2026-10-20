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

// ── Contact Log (Phase 1.5, ADR 0017) ───────────────────────────────────

public sealed record ContactLogLinkInput(string RecordIdentifier, string? ModuleName, string? Notes);
public sealed record ContactLogLinkDto(Guid Id, string RecordIdentifier, string? ModuleName, string? Notes);

public sealed record ContactLogEntryDto(
    Guid Id, Guid ParcelId, string Number, DateTime ContactedAt, string? Mode, string? ContactedBy, string? ContactPerson, string? Notes,
    IReadOnlyList<ContactLogLinkDto> Links, DateTime CreatedAt, string? AuthorName);

internal static class ContactLogRules
{
    public static DateTime Utc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc),
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    /// <summary>One adder per parcel at a time in this process, so ten people logging at once queue rather than collide on the sequence.</summary>
    public static SemaphoreSlim Gate(Guid parcelId) => Gates.GetOrAdd(parcelId, _ => new SemaphoreSlim(1, 1));

    public static string NumberOf(string parcelNumber, int sequence) => $"CL-{parcelNumber}-{sequence:000}";

    public static ContactLogEntryDto ToDto(ParcelContactLogEntry e, IReadOnlyDictionary<Guid, string> authors) =>
        new(e.Id, e.ParcelId, e.Number, e.ContactedAt, e.Mode, e.ContactedBy, e.ContactPerson, e.Notes,
            e.Links.OrderBy(l => l.CreatedAt).ThenBy(l => l.RecordIdentifier).Select(l => new ContactLogLinkDto(l.Id, l.RecordIdentifier, l.ModuleName, l.Notes)).ToList(),
            e.CreatedAt, authors.TryGetValue(e.CreatedBy, out var n) ? n : null);

    public static List<ParcelContactLogLink> BuildLinks(IReadOnlyList<ContactLogLinkInput>? links) =>
        (links ?? Array.Empty<ContactLogLinkInput>())
        .Select(l => new ParcelContactLogLink { RecordIdentifier = l.RecordIdentifier.Trim(), ModuleName = ParcelRecordHelpers.Clean(l.ModuleName), Notes = ParcelRecordHelpers.Clean(l.Notes) })
        .ToList();
}

public interface IContactLogFields
{
    DateTime ContactedAt { get; }
    string? Mode { get; }
    string? ContactedBy { get; }
    string? ContactPerson { get; }
    string? Notes { get; }
    IReadOnlyList<ContactLogLinkInput>? Links { get; }
}

public abstract class ContactLogFieldsValidator<T> : AbstractValidator<T> where T : IContactLogFields
{
    protected ContactLogFieldsValidator()
    {
        RuleFor(x => x.ContactedAt).Must(t => t.Year >= 1990 && t.Year <= 2100).WithMessage("Enter the date and time of the contact.");
        RuleFor(x => x.Mode).MaximumLength(100);
        RuleFor(x => x.ContactedBy).MaximumLength(200);
        RuleFor(x => x.ContactPerson).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(4000);
        RuleFor(x => x.Links).Must(l => l is null || l.Count <= 50).WithMessage("A contact can refer to at most 50 records.");
        RuleForEach(x => x.Links).ChildRules(l =>
        {
            l.RuleFor(i => i.RecordIdentifier).NotEmpty().WithMessage("Each linked record needs an identifier.").MaximumLength(100);
            l.RuleFor(i => i.ModuleName).MaximumLength(100);
            l.RuleFor(i => i.Notes).MaximumLength(500);
        });
    }
}

public sealed record ListContactLogQuery(Guid ParcelId) : IRequest<IReadOnlyList<ContactLogEntryDto>>;

public sealed class ListContactLogQueryHandler : IRequestHandler<ListContactLogQuery, IReadOnlyList<ContactLogEntryDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public ListContactLogQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<IReadOnlyList<ContactLogEntryDto>> Handle(ListContactLogQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ContactLog, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var rows = await _db.ParcelContactLogEntries.Include(e => e.Links).Where(e => e.ParcelId == parcel.Id).ToListAsync(ct);
        var authors = await ParcelRecordHelpers.AuthorsAsync(_db, rows.Select(r => r.CreatedBy), ct);
        return rows.OrderByDescending(e => e.ContactedAt).ThenByDescending(e => e.Sequence).Select(e => ContactLogRules.ToDto(e, authors)).ToList();
    }
}

public sealed record AddContactLogEntryCommand(
    Guid ParcelId, DateTime ContactedAt, string? Mode, string? ContactedBy, string? ContactPerson, string? Notes, IReadOnlyList<ContactLogLinkInput>? Links)
    : IRequest<ContactLogEntryDto>, IContactLogFields;

public sealed class AddContactLogEntryCommandValidator : ContactLogFieldsValidator<AddContactLogEntryCommand>
{
    public AddContactLogEntryCommandValidator() => RuleFor(x => x.ParcelId).NotEmpty();
}

public sealed class AddContactLogEntryCommandHandler : IRequestHandler<AddContactLogEntryCommand, ContactLogEntryDto>
{
    private const int MaxAttempts = 6;
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public AddContactLogEntryCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ContactLogEntryDto> Handle(AddContactLogEntryCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ContactLog, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);

        // Two people adding at once can pick the same sequence; the lock queues them in this process and the unique index
        // refuses a collision with another instance, which simply chooses again.
        var gate = ContactLogRules.Gate(parcel.Id);
        await gate.WaitAsync(ct);
        try
        {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var sequence = (await _db.ParcelContactLogEntries.Where(e => e.ParcelId == parcel.Id).MaxAsync(e => (int?)e.Sequence, ct) ?? 0) + 1;
            var entry = new ParcelContactLogEntry
            {
                ParcelId = parcel.Id, Sequence = sequence, Number = ContactLogRules.NumberOf(parcel.Number, sequence),
                ContactedAt = ContactLogRules.Utc(request.ContactedAt), Mode = ParcelRecordHelpers.Clean(request.Mode),
                ContactedBy = ParcelRecordHelpers.Clean(request.ContactedBy), ContactPerson = ParcelRecordHelpers.Clean(request.ContactPerson),
                Notes = ParcelRecordHelpers.Clean(request.Notes), Links = ContactLogRules.BuildLinks(request.Links),
            };
            _db.ParcelContactLogEntries.Add(entry);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
                continue;
            }
            var authors = await ParcelRecordHelpers.AuthorsAsync(_db, new[] { entry.CreatedBy }, ct);
            return ContactLogRules.ToDto(entry, authors);
        }
        }
        finally
        {
            gate.Release();
        }
        throw new ConflictException("contact_log_busy", "The system is busy numbering contacts. Please try again.");
    }
}

public sealed record UpdateContactLogEntryCommand(
    Guid ParcelId, Guid EntryId, DateTime ContactedAt, string? Mode, string? ContactedBy, string? ContactPerson, string? Notes, IReadOnlyList<ContactLogLinkInput>? Links)
    : IRequest<ContactLogEntryDto>, IContactLogFields;

public sealed class UpdateContactLogEntryCommandValidator : ContactLogFieldsValidator<UpdateContactLogEntryCommand>
{
    public UpdateContactLogEntryCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.EntryId).NotEmpty();
    }
}

public sealed class UpdateContactLogEntryCommandHandler : IRequestHandler<UpdateContactLogEntryCommand, ContactLogEntryDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public UpdateContactLogEntryCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<ContactLogEntryDto> Handle(UpdateContactLogEntryCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ContactLog, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var entry = await _db.ParcelContactLogEntries.Include(e => e.Links).FirstOrDefaultAsync(e => e.Id == request.EntryId && e.ParcelId == parcel.Id, ct)
                    ?? throw new NotFoundException("Contact log entry", request.EntryId);

        entry.ContactedAt = ContactLogRules.Utc(request.ContactedAt);
        entry.Mode = ParcelRecordHelpers.Clean(request.Mode);
        entry.ContactedBy = ParcelRecordHelpers.Clean(request.ContactedBy);
        entry.ContactPerson = ParcelRecordHelpers.Clean(request.ContactPerson);
        entry.Notes = ParcelRecordHelpers.Clean(request.Notes);
        _db.ParcelContactLogLinks.RemoveRange(entry.Links);
        var links = ContactLogRules.BuildLinks(request.Links);
        foreach (var l in links) l.ContactLogEntryId = entry.Id;
        _db.ParcelContactLogLinks.AddRange(links);
        await _db.SaveChangesAsync(ct);

        var fresh = await _db.ParcelContactLogEntries.Include(e => e.Links).AsNoTracking().FirstAsync(e => e.Id == entry.Id, ct);
        var authors = await ParcelRecordHelpers.AuthorsAsync(_db, new[] { fresh.CreatedBy }, ct);
        return ContactLogRules.ToDto(fresh, authors);
    }
}

public sealed record RemoveContactLogEntryCommand(Guid ParcelId, Guid EntryId) : IRequest;

public sealed class RemoveContactLogEntryCommandHandler : IRequestHandler<RemoveContactLogEntryCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveContactLogEntryCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveContactLogEntryCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ContactLog, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var entry = await _db.ParcelContactLogEntries.Include(e => e.Links).FirstOrDefaultAsync(e => e.Id == request.EntryId && e.ParcelId == parcel.Id, ct)
                    ?? throw new NotFoundException("Contact log entry", request.EntryId);
        _db.ParcelContactLogLinks.RemoveRange(entry.Links);
        _db.ParcelContactLogEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
    }
}
