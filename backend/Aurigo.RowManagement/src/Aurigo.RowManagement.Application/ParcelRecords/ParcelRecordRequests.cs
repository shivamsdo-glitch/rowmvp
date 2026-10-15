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

namespace Aurigo.RowManagement.Application.ParcelRecords;

// ── DTOs ────────────────────────────────────────────────────────────────

public sealed record TeamMemberDto(
    Guid Id, Guid ParcelId, TeamRole Role, string Name, string? Organization, bool IsConsultant, string? Email, string? Phone, string? Notes, DateTime UpdatedAt);

/// <summary><see cref="AuthorName"/> is who made the entry, when the account is known.</summary>
public sealed record DiaryEntryDto(
    Guid Id, Guid ParcelId, DateOnly OccurredOn, DiaryMethod Method, DiaryDirection Direction, string ContactName, Guid? PartyId,
    string Summary, string? Outcome, DateTime CreatedAt, Guid CreatedBy, string? AuthorName);

public sealed record ParcelNoteDto(Guid Id, Guid ParcelId, string Text, DateTime CreatedAt, Guid CreatedBy, string? AuthorName);

internal static class ParcelRecordHelpers
{
    public static async Task<IReadOnlyDictionary<Guid, string>> AuthorsAsync(IRowDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        if (wanted.Count == 0) return new Dictionary<Guid, string>();
        var users = await db.AppUsers.Where(u => wanted.Contains(u.Id)).ToListAsync(ct);
        return users.ToDictionary(u => u.Id, u => u.FullName);
    }

    public static TeamMemberDto ToDto(this ParcelTeamMember m) =>
        new(m.Id, m.ParcelId, m.Role, m.Name, m.Organization, m.IsConsultant, m.Email, m.Phone, m.Notes, m.UpdatedAt);

    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// ── Team (ROW-019) ──────────────────────────────────────────────────────

public sealed record ListParcelTeamQuery(Guid ParcelId) : IRequest<IReadOnlyList<TeamMemberDto>>;

public sealed class ListParcelTeamQueryHandler : IRequestHandler<ListParcelTeamQuery, IReadOnlyList<TeamMemberDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public ListParcelTeamQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<IReadOnlyList<TeamMemberDto>> Handle(ListParcelTeamQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        // Roles are stored as text, so the order (project lead first) is applied here, not by the database.
        return (await _db.ParcelTeamMembers.Where(m => m.ParcelId == parcel.Id).ToListAsync(ct)).OrderBy(m => m.Role).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(m => m.ToDto()).ToList();
    }
}

public interface ITeamFields
{
    TeamRole Role { get; }
    string Name { get; }
    string? Organization { get; }
    bool IsConsultant { get; }
    string? Email { get; }
    string? Phone { get; }
    string? Notes { get; }
}

public abstract class TeamFieldsValidator<T> : AbstractValidator<T> where T : ITeamFields
{
    protected TeamFieldsValidator()
    {
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(200);
        RuleFor(x => x.Organization).MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().WithMessage("Enter a valid email address.").MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).Matches(@"^[\d\s().+\-xX#]{3,40}$").WithMessage("Enter a valid phone number.").When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed record AddTeamMemberCommand(
    Guid ParcelId, TeamRole Role, string Name, string? Organization, bool IsConsultant, string? Email, string? Phone, string? Notes)
    : IRequest<TeamMemberDto>, ITeamFields;

public sealed class AddTeamMemberCommandValidator : TeamFieldsValidator<AddTeamMemberCommand>
{
    public AddTeamMemberCommandValidator() => RuleFor(x => x.ParcelId).NotEmpty();
}

public sealed class AddTeamMemberCommandHandler : IRequestHandler<AddTeamMemberCommand, TeamMemberDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public AddTeamMemberCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<TeamMemberDto> Handle(AddTeamMemberCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var name = ParcelRecordRules.CleanName(request.Name);
        var key = ParcelRecordRules.NameKey(name);
        if (await _db.ParcelTeamMembers.AnyAsync(m => m.ParcelId == parcel.Id && m.Role == request.Role && m.NameKey == key, ct))
            throw new ConflictException("team_member_exists", $"{name} is already the {ParcelRecordRules.DescribeRole(request.Role)} on this parcel.");

        var member = new ParcelTeamMember
        {
            ParcelId = parcel.Id, Role = request.Role, Name = name, NameKey = key, Organization = ParcelRecordHelpers.Clean(request.Organization),
            IsConsultant = request.IsConsultant, Email = ParcelRecordHelpers.Clean(request.Email), Phone = ParcelRecordHelpers.Clean(request.Phone),
            Notes = ParcelRecordHelpers.Clean(request.Notes),
        };
        _db.ParcelTeamMembers.Add(member);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("team_member_exists", $"{name} is already the {ParcelRecordRules.DescribeRole(request.Role)} on this parcel.");
        }
        return member.ToDto();
    }
}

public sealed record UpdateTeamMemberCommand(
    Guid ParcelId, Guid MemberId, TeamRole Role, string Name, string? Organization, bool IsConsultant, string? Email, string? Phone, string? Notes)
    : IRequest<TeamMemberDto>, ITeamFields;

public sealed class UpdateTeamMemberCommandValidator : TeamFieldsValidator<UpdateTeamMemberCommand>
{
    public UpdateTeamMemberCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.MemberId).NotEmpty();
    }
}

public sealed class UpdateTeamMemberCommandHandler : IRequestHandler<UpdateTeamMemberCommand, TeamMemberDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public UpdateTeamMemberCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<TeamMemberDto> Handle(UpdateTeamMemberCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var member = await _db.ParcelTeamMembers.FirstOrDefaultAsync(m => m.Id == request.MemberId && m.ParcelId == parcel.Id, ct)
                     ?? throw new NotFoundException("Team member", request.MemberId);
        var name = ParcelRecordRules.CleanName(request.Name);
        var key = ParcelRecordRules.NameKey(name);
        if (await _db.ParcelTeamMembers.AnyAsync(m => m.ParcelId == parcel.Id && m.Role == request.Role && m.NameKey == key && m.Id != member.Id, ct))
            throw new ConflictException("team_member_exists", $"{name} is already the {ParcelRecordRules.DescribeRole(request.Role)} on this parcel.");

        member.Role = request.Role;
        member.Name = name;
        member.NameKey = key;
        member.Organization = ParcelRecordHelpers.Clean(request.Organization);
        member.IsConsultant = request.IsConsultant;
        member.Email = ParcelRecordHelpers.Clean(request.Email);
        member.Phone = ParcelRecordHelpers.Clean(request.Phone);
        member.Notes = ParcelRecordHelpers.Clean(request.Notes);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("team_member_exists", $"{name} is already the {ParcelRecordRules.DescribeRole(request.Role)} on this parcel.");
        }
        return member.ToDto();
    }
}

public sealed record RemoveTeamMemberCommand(Guid ParcelId, Guid MemberId) : IRequest;

public sealed class RemoveTeamMemberCommandHandler : IRequestHandler<RemoveTeamMemberCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveTeamMemberCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveTeamMemberCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var member = await _db.ParcelTeamMembers.FirstOrDefaultAsync(m => m.Id == request.MemberId && m.ParcelId == parcel.Id, ct)
                     ?? throw new NotFoundException("Team member", request.MemberId);
        _db.ParcelTeamMembers.Remove(member);
        await _db.SaveChangesAsync(ct);
    }
}

// ── Negotiation diary (ROW-020) ─────────────────────────────────────────
// Read, add and remove are governed by the Negotiations page: the negotiator keeps the diary, other roles may read it.

public sealed record ListParcelDiaryQuery(Guid ParcelId) : IRequest<IReadOnlyList<DiaryEntryDto>>;

public sealed class ListParcelDiaryQueryHandler : IRequestHandler<ListParcelDiaryQuery, IReadOnlyList<DiaryEntryDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public ListParcelDiaryQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<IReadOnlyList<DiaryEntryDto>> Handle(ListParcelDiaryQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Negotiations, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var rows = await _db.ParcelDiaryEntries.Where(e => e.ParcelId == parcel.Id).OrderByDescending(e => e.OccurredOn).ThenByDescending(e => e.CreatedAt).ToListAsync(ct);
        var authors = await ParcelRecordHelpers.AuthorsAsync(_db, rows.Select(r => r.CreatedBy), ct);
        return rows.Select(e => new DiaryEntryDto(e.Id, e.ParcelId, e.OccurredOn, e.Method, e.Direction, e.ContactName, e.PartyId, e.Summary, e.Outcome,
            e.CreatedAt, e.CreatedBy, authors.GetValueOrDefault(e.CreatedBy))).ToList();
    }
}

public sealed record AddDiaryEntryCommand(
    Guid ParcelId, DateOnly OccurredOn, DiaryMethod Method, DiaryDirection Direction, string? ContactName, Guid? PartyId, string Summary, string? Outcome)
    : IRequest<DiaryEntryDto>;

public sealed class AddDiaryEntryCommandValidator : AbstractValidator<AddDiaryEntryCommand>
{
    public AddDiaryEntryCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.OccurredOn).Must(d => d.Year >= 1990 && d.Year <= 2100).WithMessage("Enter a valid date.");
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.ContactName).MaximumLength(200);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.ContactName) || x.PartyId is not null).OverridePropertyName("ContactName")
            .WithMessage("Say who was contacted, or choose a party.");
        RuleFor(x => x.Summary).NotEmpty().WithMessage("Summary is required.").MaximumLength(ParcelRecordRules.MaxSummary);
        RuleFor(x => x.Outcome).MaximumLength(ParcelRecordRules.MaxOutcome);
    }
}

public sealed class AddDiaryEntryCommandHandler : IRequestHandler<AddDiaryEntryCommand, DiaryEntryDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;

    public AddDiaryEntryCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _user = user;
    }

    public async Task<DiaryEntryDto> Handle(AddDiaryEntryCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Negotiations, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var today = await _parcels.TodayForProjectAsync(parcel.ProjectId, ct);
        if (ParcelRecordRules.DiaryDateProblem(request.OccurredOn, today) is { } problem)
            throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure(nameof(request.OccurredOn), problem) });

        var name = ParcelRecordHelpers.Clean(request.ContactName);
        if (request.PartyId is { } partyId)
        {
            var party = await _db.Parties.FirstOrDefaultAsync(p => p.Id == partyId, ct) ?? throw new NotFoundException("Party", partyId);
            name ??= party.Name;
        }
        var entry = new ParcelDiaryEntry
        {
            ParcelId = parcel.Id, OccurredOn = request.OccurredOn, Method = request.Method, Direction = request.Direction, ContactName = ParcelRecordRules.CleanName(name!),
            PartyId = request.PartyId, Summary = request.Summary.Trim(), Outcome = ParcelRecordHelpers.Clean(request.Outcome),
        };
        _db.ParcelDiaryEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        var author = await ParcelRecordHelpers.AuthorsAsync(_db, new[] { _user.Id }, ct);
        return new DiaryEntryDto(entry.Id, entry.ParcelId, entry.OccurredOn, entry.Method, entry.Direction, entry.ContactName, entry.PartyId, entry.Summary,
            entry.Outcome, entry.CreatedAt, entry.CreatedBy, author.GetValueOrDefault(entry.CreatedBy));
    }
}

public sealed record RemoveDiaryEntryCommand(Guid ParcelId, Guid EntryId) : IRequest;

public sealed class RemoveDiaryEntryCommandHandler : IRequestHandler<RemoveDiaryEntryCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveDiaryEntryCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveDiaryEntryCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Negotiations, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var entry = await _db.ParcelDiaryEntries.FirstOrDefaultAsync(e => e.Id == request.EntryId && e.ParcelId == parcel.Id, ct)
                    ?? throw new NotFoundException("Diary entry", request.EntryId);
        _db.ParcelDiaryEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
    }
}

// ── Notes (ROW-021) ─────────────────────────────────────────────────────

public sealed record ListParcelNotesQuery(Guid ParcelId) : IRequest<IReadOnlyList<ParcelNoteDto>>;

public sealed class ListParcelNotesQueryHandler : IRequestHandler<ListParcelNotesQuery, IReadOnlyList<ParcelNoteDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public ListParcelNotesQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<IReadOnlyList<ParcelNoteDto>> Handle(ListParcelNotesQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var rows = await _db.ParcelNotes.Where(n => n.ParcelId == parcel.Id).OrderByDescending(n => n.CreatedAt).ToListAsync(ct);
        var authors = await ParcelRecordHelpers.AuthorsAsync(_db, rows.Select(r => r.CreatedBy), ct);
        return rows.Select(n => new ParcelNoteDto(n.Id, n.ParcelId, n.Text, n.CreatedAt, n.CreatedBy, authors.GetValueOrDefault(n.CreatedBy))).ToList();
    }
}

public sealed record AddParcelNoteCommand(Guid ParcelId, string Text) : IRequest<ParcelNoteDto>;

public sealed class AddParcelNoteCommandValidator : AbstractValidator<AddParcelNoteCommand>
{
    public AddParcelNoteCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.Text).NotEmpty().WithMessage("Write the note first.").MaximumLength(ParcelRecordRules.MaxNote);
    }
}

public sealed class AddParcelNoteCommandHandler : IRequestHandler<AddParcelNoteCommand, ParcelNoteDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;
    private readonly ICurrentUser _user;

    public AddParcelNoteCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels, ICurrentUser user)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
        _user = user;
    }

    public async Task<ParcelNoteDto> Handle(AddParcelNoteCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var note = new ParcelNote { ParcelId = parcel.Id, Text = request.Text.Trim() };
        _db.ParcelNotes.Add(note);
        await _db.SaveChangesAsync(ct);
        var author = await ParcelRecordHelpers.AuthorsAsync(_db, new[] { _user.Id }, ct);
        return new ParcelNoteDto(note.Id, note.ParcelId, note.Text, note.CreatedAt, note.CreatedBy, author.GetValueOrDefault(note.CreatedBy));
    }
}

public sealed record RemoveParcelNoteCommand(Guid ParcelId, Guid NoteId) : IRequest;

public sealed class RemoveParcelNoteCommandHandler : IRequestHandler<RemoveParcelNoteCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveParcelNoteCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveParcelNoteCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Parcels, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var note = await _db.ParcelNotes.FirstOrDefaultAsync(n => n.Id == request.NoteId && n.ParcelId == parcel.Id, ct)
                   ?? throw new NotFoundException("Note", request.NoteId);
        _db.ParcelNotes.Remove(note);
        await _db.SaveChangesAsync(ct);
    }
}
