using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.ExternalServices;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Projects;

// The two lists the project form picks from (Phase 1.5 slice 4, ADR 0016). Both need Read on Projects.

public sealed record ListAssignableManagersQuery : IRequest<IReadOnlyList<AssignableManagerDto>>;

public sealed class ListAssignableManagersQueryHandler : IRequestHandler<ListAssignableManagersQuery, IReadOnlyList<AssignableManagerDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    public ListAssignableManagersQueryHandler(IRowDbContext db, IPagePermissionService permissions) { _db = db; _permissions = permissions; }

    public async Task<IReadOnlyList<AssignableManagerDto>> Handle(ListAssignableManagersQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Read, ct);
        var users = await _db.AppUsers.Where(u => u.IsActive && !u.IsLocked).OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ToListAsync(ct);
        return users.Select(u => new AssignableManagerDto(u.Id, u.FullName)).ToList();
    }
}

public sealed record CatalogProjectDto(string Source, string Number, string Name);

public sealed record SearchProjectCatalogQuery(string Source, string? Search) : IRequest<IReadOnlyList<CatalogProjectDto>>;

public sealed class SearchProjectCatalogQueryValidator : AbstractValidator<SearchProjectCatalogQuery>
{
    public SearchProjectCatalogQueryValidator()
    {
        RuleFor(x => x.Source).Must(CatalogSources.IsKnown).WithMessage("Source must be Planned or Build.");
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public sealed class SearchProjectCatalogQueryHandler : IRequestHandler<SearchProjectCatalogQuery, IReadOnlyList<CatalogProjectDto>>
{
    private readonly IPagePermissionService _permissions;
    private readonly IProjectCatalogClient _catalog;
    public SearchProjectCatalogQueryHandler(IPagePermissionService permissions, IProjectCatalogClient catalog) { _permissions = permissions; _catalog = catalog; }

    public async Task<IReadOnlyList<CatalogProjectDto>> Handle(SearchProjectCatalogQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Read, ct);
        return (await _catalog.SearchAsync(request.Source, request.Search, ct)).Select(p => new CatalogProjectDto(p.Source, p.Number, p.Name)).ToList();
    }
}
