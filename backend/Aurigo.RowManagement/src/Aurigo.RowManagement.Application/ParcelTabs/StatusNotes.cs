using Aurigo.RowManagement.Application.ParcelRecords;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.ParcelTabs;

public sealed record StatusNoteInput(DateOnly NoteDate, string Text);
public sealed record StatusNoteDto(Guid Id, DateOnly NoteDate, string Text);

/// <summary>The plain dated status notes kept on a condemnation, property management record or relocation (ADR 0020).</summary>
internal static class StatusNoteRules
{
    public const int MaxRows = 50;

    public static async Task<IReadOnlyList<StatusNoteDto>> LoadAsync(IRowDbContext db, string kind, Guid ownerId, CancellationToken ct) =>
        (await db.RecordStatusNotes.AsNoTracking().Where(n => n.OwnerKind == kind && n.OwnerId == ownerId).ToListAsync(ct))
            .OrderBy(n => n.NoteDate).ThenBy(n => n.CreatedAt).Select(n => new StatusNoteDto(n.Id, n.NoteDate, n.Text)).ToList();

    /// <summary>Replaces the notes of one owner with the ones sent.</summary>
    public static async Task ReplaceAsync(IRowDbContext db, string kind, Guid ownerId, IReadOnlyList<StatusNoteInput>? input, CancellationToken ct)
    {
        var existing = await db.RecordStatusNotes.Where(n => n.OwnerKind == kind && n.OwnerId == ownerId).ToListAsync(ct);
        db.RecordStatusNotes.RemoveRange(existing);
        db.RecordStatusNotes.AddRange((input ?? Array.Empty<StatusNoteInput>()).Select(n => new RecordStatusNote { OwnerKind = kind, OwnerId = ownerId, NoteDate = n.NoteDate, Text = n.Text.Trim() }));
    }

    public static async Task DeleteAsync(IRowDbContext db, string kind, Guid ownerId, CancellationToken ct) =>
        db.RecordStatusNotes.RemoveRange(await db.RecordStatusNotes.Where(n => n.OwnerKind == kind && n.OwnerId == ownerId).ToListAsync(ct));

    public static void AddRules<T>(AbstractValidator<T> v, Func<T, IReadOnlyList<StatusNoteInput>?> notes)
    {
        v.RuleFor(x => notes(x)).Must(r => r is null || r.Count <= MaxRows).WithMessage("At most 50 status notes.").OverridePropertyName("StatusNotes");
        v.RuleForEach(x => notes(x)).ChildRules(r => r.RuleFor(i => i.Text).NotEmpty().WithMessage("Each status note needs text.").MaximumLength(4000)).OverridePropertyName("StatusNotes");
    }
}
