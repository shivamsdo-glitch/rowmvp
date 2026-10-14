using System.Text.Json;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.SavedFilters;

public sealed record SavedFilterDto(Guid Id, string PageKey, string Name, string FilterJson, DateTime UpdatedAt);

internal static class SavedFilterRules
{
    public const int NameMaxLength = 100;
    public const int FilterJsonMaxLength = 16 * 1024;

    public static IRuleBuilderOptions<T, string> ValidName<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Name is required.")
            .MaximumLength(NameMaxLength);

    /// <summary>The filter state must be a JSON object; its keys belong to the page's UI, so nothing else is checked.</summary>
    public static IRuleBuilderOptions<T, string> ValidFilterJson<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MaximumLength(FilterJsonMaxLength)
            .Must(IsJsonObject).WithMessage("FilterJson must be a valid JSON object.");

    private static bool IsJsonObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static SavedFilterDto ToDto(SavedFilterProfile p) => new(p.Id, p.PageKey, p.Name, p.FilterJson, p.UpdatedAt);
}

// ── List ────────────────────────────────────────────────────────────────

/// <summary>The caller's own saved filters for a page, by name.</summary>
public sealed record ListSavedFiltersQuery(string PageKey) : IRequest<IReadOnlyList<SavedFilterDto>>;

public sealed class ListSavedFiltersQueryValidator : AbstractValidator<ListSavedFiltersQuery>
{
    public ListSavedFiltersQueryValidator() =>
        RuleFor(x => x.PageKey).Must(PageKeys.IsKnown).WithMessage("Unknown page.");
}

public sealed class ListSavedFiltersQueryHandler : IRequestHandler<ListSavedFiltersQuery, IReadOnlyList<SavedFilterDto>>
{
    private readonly IRowDbContext _db;
    private readonly ICurrentUser _user;

    public ListSavedFiltersQueryHandler(IRowDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<IReadOnlyList<SavedFilterDto>> Handle(ListSavedFiltersQuery request, CancellationToken ct)
    {
        var rows = await _db.SavedFilterProfiles
            .Where(p => p.UserId == _user.Id && p.PageKey == request.PageKey)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
        return rows.Select(SavedFilterRules.ToDto).ToList();
    }
}

// ── Create ──────────────────────────────────────────────────────────────

public sealed record CreateSavedFilterCommand(string PageKey, string Name, string FilterJson) : IRequest<SavedFilterDto>;

public sealed class CreateSavedFilterCommandValidator : AbstractValidator<CreateSavedFilterCommand>
{
    public CreateSavedFilterCommandValidator()
    {
        RuleFor(x => x.PageKey).Must(PageKeys.IsKnown).WithMessage("Unknown page.");
        RuleFor(x => x.Name).ValidName();
        RuleFor(x => x.FilterJson).ValidFilterJson();
    }
}

public sealed class CreateSavedFilterCommandHandler : IRequestHandler<CreateSavedFilterCommand, SavedFilterDto>
{
    private readonly IRowDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPagePermissionService _permissions;

    public CreateSavedFilterCommandHandler(IRowDbContext db, ICurrentUser user, IPagePermissionService permissions)
    {
        _db = db;
        _user = user;
        _permissions = permissions;
    }

    public async Task<SavedFilterDto> Handle(CreateSavedFilterCommand request, CancellationToken ct)
    {
        // Filtering a list needs read access to it; saving one adds no further privilege.
        await _permissions.EnsureAsync(request.PageKey, PermissionAction.Read, ct);

        var name = request.Name.Trim();
        var lower = name.ToLower();
        if (await _db.SavedFilterProfiles.AnyAsync(
                p => p.UserId == _user.Id && p.PageKey == request.PageKey && p.Name.ToLower() == lower, ct))
            throw new ConflictException("saved_filter_exists", $"You already have a saved filter named '{name}' on this page.");

        var profile = new SavedFilterProfile
        {
            UserId = _user.Id,
            PageKey = request.PageKey,
            Name = name,
            FilterJson = request.FilterJson,
        };
        _db.SavedFilterProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
        return SavedFilterRules.ToDto(profile);
    }
}

// ── Update ──────────────────────────────────────────────────────────────

public sealed record UpdateSavedFilterCommand(Guid Id, string Name, string FilterJson) : IRequest<SavedFilterDto>;

public sealed class UpdateSavedFilterCommandValidator : AbstractValidator<UpdateSavedFilterCommand>
{
    public UpdateSavedFilterCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).ValidName();
        RuleFor(x => x.FilterJson).ValidFilterJson();
    }
}

public sealed class UpdateSavedFilterCommandHandler : IRequestHandler<UpdateSavedFilterCommand, SavedFilterDto>
{
    private readonly IRowDbContext _db;
    private readonly ICurrentUser _user;

    public UpdateSavedFilterCommandHandler(IRowDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<SavedFilterDto> Handle(UpdateSavedFilterCommand request, CancellationToken ct)
    {
        // Someone else's profile is reported as missing, not forbidden, so ids cannot be probed.
        var profile = await _db.SavedFilterProfiles.FirstOrDefaultAsync(p => p.Id == request.Id && p.UserId == _user.Id, ct)
                      ?? throw new NotFoundException("Saved filter", request.Id);

        var name = request.Name.Trim();
        var lower = name.ToLower();
        if (await _db.SavedFilterProfiles.AnyAsync(
                p => p.Id != profile.Id && p.UserId == _user.Id && p.PageKey == profile.PageKey && p.Name.ToLower() == lower, ct))
            throw new ConflictException("saved_filter_exists", $"You already have a saved filter named '{name}' on this page.");

        profile.Name = name;
        profile.FilterJson = request.FilterJson;
        await _db.SaveChangesAsync(ct);
        return SavedFilterRules.ToDto(profile);
    }
}

// ── Delete ──────────────────────────────────────────────────────────────

public sealed record DeleteSavedFilterCommand(Guid Id) : IRequest;

public sealed class DeleteSavedFilterCommandHandler : IRequestHandler<DeleteSavedFilterCommand>
{
    private readonly IRowDbContext _db;
    private readonly ICurrentUser _user;

    public DeleteSavedFilterCommandHandler(IRowDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task Handle(DeleteSavedFilterCommand request, CancellationToken ct)
    {
        var profile = await _db.SavedFilterProfiles.FirstOrDefaultAsync(p => p.Id == request.Id && p.UserId == _user.Id, ct)
                      ?? throw new NotFoundException("Saved filter", request.Id);

        // A saved filter is a personal preference, not a record, so it is removed outright (the audit log keeps the snapshot).
        _db.SavedFilterProfiles.Remove(profile);
        await _db.SaveChangesAsync(ct);
    }
}
