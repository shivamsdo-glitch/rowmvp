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

// ── Questionnaire (Phase 1.5, ADR 0019) ─────────────────────────────────

public sealed record QuestionnaireInterestInput(string Name, string? Phone, string? Contact, string? Email, string? Address);
public sealed record QuestionnaireQuestionInput(string Question, string? Answer, string? Notes);
public sealed record QuestionnaireInterestDto(Guid Id, string Name, string? Phone, string? Contact, string? Email, string? Address);
public sealed record QuestionnaireQuestionDto(Guid Id, string Question, string? Answer, string? Notes);

public sealed record QuestionnaireDto(
    Guid Id, Guid ParcelId, string Number, string ParcelNumber, string RelocationType, DateOnly? DueByDate, DateOnly? SentToOwnerDate,
    IReadOnlyList<QuestionnaireInterestDto> Interests, IReadOnlyList<QuestionnaireQuestionDto> Questions, DateTime UpdatedAt);

internal static class QuestionnaireRules
{
    public const int MaxRows = 50;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    /// <summary>One adder per parcel at a time in this process; the unique (parcel, sequence) index backstops other instances.</summary>
    public static SemaphoreSlim Gate(Guid parcelId) => Gates.GetOrAdd(parcelId, _ => new SemaphoreSlim(1, 1));

    public static string NumberOf(string parcelNumber, int sequence) => $"QST-{parcelNumber}-{sequence:000}";

    public static QuestionnaireDto ToDto(ParcelQuestionnaire q, string parcelNumber) =>
        new(q.Id, q.ParcelId, q.Number, parcelNumber, q.RelocationType, q.DueByDate, q.SentToOwnerDate,
            q.Interests.OrderBy(i => i.CreatedAt).ThenBy(i => i.Name).Select(i => new QuestionnaireInterestDto(i.Id, i.Name, i.Phone, i.Contact, i.Email, i.Address)).ToList(),
            q.Questions.OrderBy(i => i.CreatedAt).Select(i => new QuestionnaireQuestionDto(i.Id, i.Question, i.Answer, i.Notes)).ToList(), q.UpdatedAt);

    public static void Apply(ParcelQuestionnaire q, IQuestionnaireFields f)
    {
        q.RelocationType = f.RelocationType.Trim();
        q.DueByDate = f.DueByDate;
        q.SentToOwnerDate = f.SentToOwnerDate;
    }

    public static List<QuestionnaireInterest> BuildInterests(IReadOnlyList<QuestionnaireInterestInput>? input) =>
        (input ?? Array.Empty<QuestionnaireInterestInput>()).Select(i => new QuestionnaireInterest
        {
            Name = i.Name.Trim(), Phone = ParcelRecordHelpers.Clean(i.Phone), Contact = ParcelRecordHelpers.Clean(i.Contact), Email = ParcelRecordHelpers.Clean(i.Email), Address = ParcelRecordHelpers.Clean(i.Address),
        }).ToList();

    public static List<QuestionnaireQuestion> BuildQuestions(IReadOnlyList<QuestionnaireQuestionInput>? input) =>
        (input ?? Array.Empty<QuestionnaireQuestionInput>()).Select(i => new QuestionnaireQuestion
        {
            Question = i.Question.Trim(), Answer = ParcelRecordHelpers.Clean(i.Answer), Notes = ParcelRecordHelpers.Clean(i.Notes),
        }).ToList();
}

public interface IQuestionnaireFields
{
    string RelocationType { get; }
    DateOnly? DueByDate { get; }
    DateOnly? SentToOwnerDate { get; }
    IReadOnlyList<QuestionnaireInterestInput>? Interests { get; }
    IReadOnlyList<QuestionnaireQuestionInput>? Questions { get; }
}

public abstract class QuestionnaireFieldsValidator<T> : AbstractValidator<T> where T : IQuestionnaireFields
{
    protected QuestionnaireFieldsValidator()
    {
        RuleFor(x => x.RelocationType).NotEmpty().WithMessage("Relocation type is required.").MaximumLength(100);
        RuleFor(x => x.Interests).Must(r => r is null || r.Count <= QuestionnaireRules.MaxRows).WithMessage("A questionnaire can have at most 50 ownership interests.");
        RuleForEach(x => x.Interests).ChildRules(r =>
        {
            r.RuleFor(i => i.Name).NotEmpty().WithMessage("Each ownership interest needs a name.").MaximumLength(200);
            r.RuleFor(i => i.Phone).MaximumLength(100);
            r.RuleFor(i => i.Contact).MaximumLength(200);
            r.RuleFor(i => i.Email).MaximumLength(200);
            r.RuleFor(i => i.Address).MaximumLength(500);
        });
        RuleFor(x => x.Questions).Must(r => r is null || r.Count <= QuestionnaireRules.MaxRows).WithMessage("A questionnaire can have at most 50 questions.");
        RuleForEach(x => x.Questions).ChildRules(r =>
        {
            r.RuleFor(i => i.Question).NotEmpty().WithMessage("Each question needs text.").MaximumLength(1000);
            r.RuleFor(i => i.Answer).MaximumLength(4000);
            r.RuleFor(i => i.Notes).MaximumLength(4000);
        });
    }
}

public sealed record ListQuestionnairesQuery(Guid ParcelId) : IRequest<IReadOnlyList<QuestionnaireDto>>;

public sealed class ListQuestionnairesQueryHandler : IRequestHandler<ListQuestionnairesQuery, IReadOnlyList<QuestionnaireDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public ListQuestionnairesQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<IReadOnlyList<QuestionnaireDto>> Handle(ListQuestionnairesQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelQuestionnaire, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var rows = await _db.ParcelQuestionnaires.Include(q => q.Interests).Include(q => q.Questions).AsNoTracking().Where(q => q.ParcelId == parcel.Id).ToListAsync(ct);
        return rows.OrderBy(q => q.Sequence).Select(q => QuestionnaireRules.ToDto(q, parcel.Number)).ToList();
    }
}

public sealed record AddQuestionnaireCommand(
    Guid ParcelId, string RelocationType, DateOnly? DueByDate, DateOnly? SentToOwnerDate, IReadOnlyList<QuestionnaireInterestInput>? Interests,
    IReadOnlyList<QuestionnaireQuestionInput>? Questions) : IRequest<QuestionnaireDto>, IQuestionnaireFields;

public sealed class AddQuestionnaireCommandValidator : QuestionnaireFieldsValidator<AddQuestionnaireCommand>
{
    public AddQuestionnaireCommandValidator() => RuleFor(x => x.ParcelId).NotEmpty();
}

public sealed class AddQuestionnaireCommandHandler : IRequestHandler<AddQuestionnaireCommand, QuestionnaireDto>
{
    private const int MaxAttempts = 6;
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public AddQuestionnaireCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<QuestionnaireDto> Handle(AddQuestionnaireCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelQuestionnaire, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var gate = QuestionnaireRules.Gate(parcel.Id);
        await gate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var sequence = (await _db.ParcelQuestionnaires.Where(q => q.ParcelId == parcel.Id).MaxAsync(q => (int?)q.Sequence, ct) ?? 0) + 1;
                var q = new ParcelQuestionnaire
                {
                    ParcelId = parcel.Id, Sequence = sequence, Number = QuestionnaireRules.NumberOf(parcel.Number, sequence),
                    Interests = QuestionnaireRules.BuildInterests(request.Interests), Questions = QuestionnaireRules.BuildQuestions(request.Questions),
                };
                QuestionnaireRules.Apply(q, request);
                _db.ParcelQuestionnaires.Add(q);
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    _db.ChangeTracker.Clear();
                    continue;
                }
                return QuestionnaireRules.ToDto(q, parcel.Number);
            }
        }
        finally
        {
            gate.Release();
        }
        throw new ConflictException("questionnaire_busy", "The system is busy numbering questionnaires. Please try again.");
    }
}

public sealed record UpdateQuestionnaireCommand(
    Guid ParcelId, Guid QuestionnaireId, string RelocationType, DateOnly? DueByDate, DateOnly? SentToOwnerDate, IReadOnlyList<QuestionnaireInterestInput>? Interests,
    IReadOnlyList<QuestionnaireQuestionInput>? Questions) : IRequest<QuestionnaireDto>, IQuestionnaireFields;

public sealed class UpdateQuestionnaireCommandValidator : QuestionnaireFieldsValidator<UpdateQuestionnaireCommand>
{
    public UpdateQuestionnaireCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.QuestionnaireId).NotEmpty();
    }
}

public sealed class UpdateQuestionnaireCommandHandler : IRequestHandler<UpdateQuestionnaireCommand, QuestionnaireDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public UpdateQuestionnaireCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<QuestionnaireDto> Handle(UpdateQuestionnaireCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelQuestionnaire, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var q = await _db.ParcelQuestionnaires.Include(x => x.Interests).Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == request.QuestionnaireId && x.ParcelId == parcel.Id, ct)
                ?? throw new NotFoundException("Questionnaire", request.QuestionnaireId);
        QuestionnaireRules.Apply(q, request);
        _db.QuestionnaireInterests.RemoveRange(q.Interests);
        _db.QuestionnaireQuestions.RemoveRange(q.Questions);
        var interests = QuestionnaireRules.BuildInterests(request.Interests);
        var questions = QuestionnaireRules.BuildQuestions(request.Questions);
        foreach (var i in interests) i.QuestionnaireId = q.Id;
        foreach (var i in questions) i.QuestionnaireId = q.Id;
        _db.QuestionnaireInterests.AddRange(interests);
        _db.QuestionnaireQuestions.AddRange(questions);
        await _db.SaveChangesAsync(ct);

        var fresh = await _db.ParcelQuestionnaires.Include(x => x.Interests).Include(x => x.Questions).AsNoTracking().FirstAsync(x => x.Id == q.Id, ct);
        return QuestionnaireRules.ToDto(fresh, parcel.Number);
    }
}

public sealed record RemoveQuestionnaireCommand(Guid ParcelId, Guid QuestionnaireId) : IRequest;

public sealed class RemoveQuestionnaireCommandHandler : IRequestHandler<RemoveQuestionnaireCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveQuestionnaireCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveQuestionnaireCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.ParcelQuestionnaire, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var q = await _db.ParcelQuestionnaires.Include(x => x.Interests).Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == request.QuestionnaireId && x.ParcelId == parcel.Id, ct)
                ?? throw new NotFoundException("Questionnaire", request.QuestionnaireId);
        _db.QuestionnaireInterests.RemoveRange(q.Interests);
        _db.QuestionnaireQuestions.RemoveRange(q.Questions);
        _db.ParcelQuestionnaires.Remove(q);
        await _db.SaveChangesAsync(ct);
    }
}
