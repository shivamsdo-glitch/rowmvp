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

// ── Title Encumbrances (Phase 1.5, ADR 0018) ────────────────────────────

public sealed record EncumbranceLinkDto(Guid Id, string RecordIdentifier, string? ModuleName, string? Notes);

public sealed record EncumbranceDto(
    Guid Id, Guid ParcelId, string Number, string EncumbranceType, string DocumentTitle, DateOnly? DocumentDate, DateOnly? DateRecorded,
    string? DocumentNumber, bool IsUnrecorded, string Grantor, string Grantee, DateOnly? ExceptionSubmittedDate, DateOnly? ExceptionApprovedDate,
    string? ImpactAnalysis, string? RecommendedCure, string? CureStatus, string? ReviewNotes, IReadOnlyList<EncumbranceLinkDto> Links, DateTime UpdatedAt);

internal static class EncumbranceRules
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    /// <summary>One adder per parcel at a time in this process; the unique (parcel, sequence) index backstops other instances.</summary>
    public static SemaphoreSlim Gate(Guid parcelId) => Gates.GetOrAdd(parcelId, _ => new SemaphoreSlim(1, 1));

    public static string NumberOf(string parcelNumber, int sequence) => $"ENC-{parcelNumber}-{sequence:000}";

    public static EncumbranceDto ToDto(ParcelEncumbrance e) =>
        new(e.Id, e.ParcelId, e.Number, e.EncumbranceType, e.DocumentTitle, e.DocumentDate, e.DateRecorded, e.DocumentNumber, e.IsUnrecorded, e.Grantor, e.Grantee,
            e.ExceptionSubmittedDate, e.ExceptionApprovedDate, e.ImpactAnalysis, e.RecommendedCure, e.CureStatus, e.ReviewNotes,
            e.Links.OrderBy(l => l.CreatedAt).ThenBy(l => l.RecordIdentifier).Select(l => new EncumbranceLinkDto(l.Id, l.RecordIdentifier, l.ModuleName, l.Notes)).ToList(), e.UpdatedAt);

    public static void Apply(ParcelEncumbrance e, IEncumbranceFields f)
    {
        e.EncumbranceType = f.EncumbranceType.Trim();
        e.DocumentTitle = f.DocumentTitle.Trim();
        e.DocumentDate = f.DocumentDate;
        e.DateRecorded = f.IsUnrecorded ? null : f.DateRecorded;
        e.DocumentNumber = ParcelRecordHelpers.Clean(f.DocumentNumber);
        e.IsUnrecorded = f.IsUnrecorded;
        e.Grantor = f.Grantor.Trim();
        e.Grantee = f.Grantee.Trim();
        e.ExceptionSubmittedDate = f.ExceptionSubmittedDate;
        e.ExceptionApprovedDate = f.ExceptionApprovedDate;
        e.ImpactAnalysis = ParcelRecordHelpers.Clean(f.ImpactAnalysis);
        e.RecommendedCure = ParcelRecordHelpers.Clean(f.RecommendedCure);
        e.CureStatus = ParcelRecordHelpers.Clean(f.CureStatus);
        e.ReviewNotes = ParcelRecordHelpers.Clean(f.ReviewNotes);
    }

    public static List<ParcelEncumbranceLink> BuildLinks(IReadOnlyList<ContactLogLinkInput>? links) =>
        (links ?? Array.Empty<ContactLogLinkInput>())
        .Select(l => new ParcelEncumbranceLink { RecordIdentifier = l.RecordIdentifier.Trim(), ModuleName = ParcelRecordHelpers.Clean(l.ModuleName), Notes = ParcelRecordHelpers.Clean(l.Notes) })
        .ToList();
}

public interface IEncumbranceFields
{
    string EncumbranceType { get; }
    string DocumentTitle { get; }
    DateOnly? DocumentDate { get; }
    DateOnly? DateRecorded { get; }
    string? DocumentNumber { get; }
    bool IsUnrecorded { get; }
    string Grantor { get; }
    string Grantee { get; }
    DateOnly? ExceptionSubmittedDate { get; }
    DateOnly? ExceptionApprovedDate { get; }
    string? ImpactAnalysis { get; }
    string? RecommendedCure { get; }
    string? CureStatus { get; }
    string? ReviewNotes { get; }
    IReadOnlyList<ContactLogLinkInput>? Links { get; }
}

public abstract class EncumbranceFieldsValidator<T> : AbstractValidator<T> where T : IEncumbranceFields
{
    protected EncumbranceFieldsValidator()
    {
        RuleFor(x => x.EncumbranceType).NotEmpty().WithMessage("Encumbrance type is required.").MaximumLength(100);
        RuleFor(x => x.DocumentTitle).NotEmpty().WithMessage("Document title is required.").MaximumLength(300);
        RuleFor(x => x.Grantor).NotEmpty().WithMessage("Grantor / lessor is required.").MaximumLength(1000);
        RuleFor(x => x.Grantee).NotEmpty().WithMessage("Grantee / lessee is required.").MaximumLength(1000);
        RuleFor(x => x.DocumentNumber).MaximumLength(500);
        RuleFor(x => x.ImpactAnalysis).MaximumLength(4000);
        RuleFor(x => x.RecommendedCure).MaximumLength(4000);
        RuleFor(x => x.CureStatus).MaximumLength(100);
        RuleFor(x => x.ReviewNotes).MaximumLength(4000);
        RuleFor(x => x.DateRecorded).Null().When(x => x.IsUnrecorded).WithMessage("An unrecorded document has no recording date.");
        RuleFor(x => x.ExceptionApprovedDate).GreaterThanOrEqualTo(x => x.ExceptionSubmittedDate!.Value)
            .When(x => x.ExceptionSubmittedDate is not null && x.ExceptionApprovedDate is not null)
            .WithMessage("The exception cannot be approved before it is submitted.");
        RuleFor(x => x.Links).Must(l => l is null || l.Count <= 50).WithMessage("An encumbrance can refer to at most 50 records.");
        RuleForEach(x => x.Links).ChildRules(l =>
        {
            l.RuleFor(i => i.RecordIdentifier).NotEmpty().WithMessage("Each linked record needs an identifier.").MaximumLength(100);
            l.RuleFor(i => i.ModuleName).MaximumLength(100);
            l.RuleFor(i => i.Notes).MaximumLength(500);
        });
    }
}

public sealed record ListEncumbrancesQuery(Guid ParcelId) : IRequest<IReadOnlyList<EncumbranceDto>>;

public sealed class ListEncumbrancesQueryHandler : IRequestHandler<ListEncumbrancesQuery, IReadOnlyList<EncumbranceDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public ListEncumbrancesQueryHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<IReadOnlyList<EncumbranceDto>> Handle(ListEncumbrancesQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.TitleEncumbrances, PermissionAction.Read, ct);
        var parcel = await _parcels.LoadAsync(request.ParcelId, ct);
        var rows = await _db.ParcelEncumbrances.Include(e => e.Links).Where(e => e.ParcelId == parcel.Id).ToListAsync(ct);
        return rows.OrderBy(e => e.Sequence).Select(EncumbranceRules.ToDto).ToList();
    }
}

public sealed record AddEncumbranceCommand(
    Guid ParcelId, string EncumbranceType, string DocumentTitle, DateOnly? DocumentDate, DateOnly? DateRecorded, string? DocumentNumber, bool IsUnrecorded,
    string Grantor, string Grantee, DateOnly? ExceptionSubmittedDate, DateOnly? ExceptionApprovedDate, string? ImpactAnalysis, string? RecommendedCure,
    string? CureStatus, string? ReviewNotes, IReadOnlyList<ContactLogLinkInput>? Links) : IRequest<EncumbranceDto>, IEncumbranceFields;

public sealed class AddEncumbranceCommandValidator : EncumbranceFieldsValidator<AddEncumbranceCommand>
{
    public AddEncumbranceCommandValidator() => RuleFor(x => x.ParcelId).NotEmpty();
}

public sealed class AddEncumbranceCommandHandler : IRequestHandler<AddEncumbranceCommand, EncumbranceDto>
{
    private const int MaxAttempts = 6;
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public AddEncumbranceCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<EncumbranceDto> Handle(AddEncumbranceCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.TitleEncumbrances, PermissionAction.Create, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);

        var gate = EncumbranceRules.Gate(parcel.Id);
        await gate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var sequence = (await _db.ParcelEncumbrances.Where(e => e.ParcelId == parcel.Id).MaxAsync(e => (int?)e.Sequence, ct) ?? 0) + 1;
                var e = new ParcelEncumbrance { ParcelId = parcel.Id, Sequence = sequence, Number = EncumbranceRules.NumberOf(parcel.Number, sequence), Links = EncumbranceRules.BuildLinks(request.Links) };
                EncumbranceRules.Apply(e, request);
                _db.ParcelEncumbrances.Add(e);
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    _db.ChangeTracker.Clear();
                    continue;
                }
                return EncumbranceRules.ToDto(e);
            }
        }
        finally
        {
            gate.Release();
        }
        throw new ConflictException("encumbrance_busy", "The system is busy numbering encumbrances. Please try again.");
    }
}

public sealed record UpdateEncumbranceCommand(
    Guid ParcelId, Guid EncumbranceId, string EncumbranceType, string DocumentTitle, DateOnly? DocumentDate, DateOnly? DateRecorded, string? DocumentNumber, bool IsUnrecorded,
    string Grantor, string Grantee, DateOnly? ExceptionSubmittedDate, DateOnly? ExceptionApprovedDate, string? ImpactAnalysis, string? RecommendedCure,
    string? CureStatus, string? ReviewNotes, IReadOnlyList<ContactLogLinkInput>? Links) : IRequest<EncumbranceDto>, IEncumbranceFields;

public sealed class UpdateEncumbranceCommandValidator : EncumbranceFieldsValidator<UpdateEncumbranceCommand>
{
    public UpdateEncumbranceCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.EncumbranceId).NotEmpty();
    }
}

public sealed class UpdateEncumbranceCommandHandler : IRequestHandler<UpdateEncumbranceCommand, EncumbranceDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public UpdateEncumbranceCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task<EncumbranceDto> Handle(UpdateEncumbranceCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.TitleEncumbrances, PermissionAction.Update, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var e = await _db.ParcelEncumbrances.Include(x => x.Links).FirstOrDefaultAsync(x => x.Id == request.EncumbranceId && x.ParcelId == parcel.Id, ct)
                ?? throw new NotFoundException("Encumbrance", request.EncumbranceId);

        EncumbranceRules.Apply(e, request);
        _db.ParcelEncumbranceLinks.RemoveRange(e.Links);
        var links = EncumbranceRules.BuildLinks(request.Links);
        foreach (var l in links) l.EncumbranceId = e.Id;
        _db.ParcelEncumbranceLinks.AddRange(links);
        await _db.SaveChangesAsync(ct);

        var fresh = await _db.ParcelEncumbrances.Include(x => x.Links).AsNoTracking().FirstAsync(x => x.Id == e.Id, ct);
        return EncumbranceRules.ToDto(fresh);
    }
}

public sealed record RemoveEncumbranceCommand(Guid ParcelId, Guid EncumbranceId) : IRequest;

public sealed class RemoveEncumbranceCommandHandler : IRequestHandler<RemoveEncumbranceCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ParcelService _parcels;

    public RemoveEncumbranceCommandHandler(IRowDbContext db, IPagePermissionService permissions, ParcelService parcels)
    {
        _db = db;
        _permissions = permissions;
        _parcels = parcels;
    }

    public async Task Handle(RemoveEncumbranceCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.TitleEncumbrances, PermissionAction.Delete, ct);
        var parcel = await _parcels.LoadActiveAsync(request.ParcelId, ct);
        var e = await _db.ParcelEncumbrances.Include(x => x.Links).FirstOrDefaultAsync(x => x.Id == request.EncumbranceId && x.ParcelId == parcel.Id, ct)
                ?? throw new NotFoundException("Encumbrance", request.EncumbranceId);
        _db.ParcelEncumbranceLinks.RemoveRange(e.Links);
        _db.ParcelEncumbrances.Remove(e);
        await _db.SaveChangesAsync(ct);
    }
}
