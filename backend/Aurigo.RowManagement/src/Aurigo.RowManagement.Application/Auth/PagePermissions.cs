using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Auth;

public sealed record PagePermissionDto(string PageKey, bool CanCreate, bool CanRead, bool CanUpdate, bool CanDelete);

/// <summary>Answers "may the current user do X on page Y?" from the role/page permission rows.</summary>
public interface IPagePermissionService
{
    /// <summary>Throws <see cref="ForbiddenException"/> unless one of the caller's roles allows the action.</summary>
    Task EnsureAsync(string pageKey, PermissionAction action, CancellationToken ct = default);

    /// <summary>Every page, with the union of the caller's roles' grants (all false when none).</summary>
    Task<IReadOnlyList<PagePermissionDto>> GetEffectiveAsync(CancellationToken ct = default);
}

public sealed class PagePermissionService : IPagePermissionService
{
    private readonly IRowDbContext _db;
    private readonly ICurrentUser _user;

    public PagePermissionService(IRowDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task EnsureAsync(string pageKey, PermissionAction action, CancellationToken ct = default)
    {
        var grants = await LoadAsync(pageKey, ct);
        if (!PermissionTemplateCalculator.Merge(grants.Select(g => g.Flags)).Allows(action))
            throw new ForbiddenException($"Your role does not allow {action.ToString().ToLowerInvariant()} on '{pageKey}'.");
    }

    public async Task<IReadOnlyList<PagePermissionDto>> GetEffectiveAsync(CancellationToken ct = default)
    {
        var byPage = (await LoadAsync(null, ct)).ToLookup(g => g.PageKey, g => g.Flags);
        return PageKeys.All
            .Select(page =>
            {
                var f = PermissionTemplateCalculator.Merge(byPage[page]);
                return new PagePermissionDto(page, f.Create, f.Read, f.Update, f.Delete);
            })
            .ToList();
    }

    private sealed record Grant(string PageKey, PermissionFlags Flags);

    private async Task<List<Grant>> LoadAsync(string? pageKey, CancellationToken ct)
    {
        var roles = _user.Roles;
        if (roles.Count == 0) return new List<Grant>();

        // Tenant-filtered: only this tenant's roles and permission rows are ever visible.
        var rows = await _db.RolePagePermissions
            .Where(p => (pageKey == null || p.PageKey == pageKey) && p.Role != null && roles.Contains(p.Role.Name))
            .Select(p => new { p.PageKey, p.CanCreate, p.CanRead, p.CanUpdate, p.CanDelete })
            .ToListAsync(ct);

        return rows
            .Select(r => new Grant(r.PageKey, new PermissionFlags(r.CanCreate, r.CanRead, r.CanUpdate, r.CanDelete)))
            .ToList();
    }
}

/// <summary>The caller's effective access on every page, for building navigation and disabling actions in the UI.</summary>
public sealed record GetMyPermissionsQuery : IRequest<IReadOnlyList<PagePermissionDto>>;

public sealed class GetMyPermissionsQueryHandler : IRequestHandler<GetMyPermissionsQuery, IReadOnlyList<PagePermissionDto>>
{
    private readonly IPagePermissionService _permissions;
    public GetMyPermissionsQueryHandler(IPagePermissionService permissions) => _permissions = permissions;

    public Task<IReadOnlyList<PagePermissionDto>> Handle(GetMyPermissionsQuery request, CancellationToken ct) =>
        _permissions.GetEffectiveAsync(ct);
}
