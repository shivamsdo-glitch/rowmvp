using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Mappers;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Projects;

internal static class ProjectChildren
{
    public const decimal MaxAmount = 1_000_000_000_000m;

    public static async Task EnsureProjectAsync(IRowDbContext db, Guid projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            throw new NotFoundException("Project", projectId);
    }
}

// ── Cost estimates, versioned (ROW-012) ─────────────────────────────────

/// <summary>Adds the next version. Earlier versions are never edited or removed.</summary>
public sealed record AddCostEstimateCommand(
    Guid ProjectId, decimal LandCost, decimal ImprovementsCost, decimal RelocationCost, decimal IndirectCost, string? Notes)
    : IRequest<CostEstimateDto>;

public sealed class AddCostEstimateCommandValidator : AbstractValidator<AddCostEstimateCommand>
{
    public AddCostEstimateCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.LandCost).InclusiveBetween(0m, ProjectChildren.MaxAmount);
        RuleFor(x => x.ImprovementsCost).InclusiveBetween(0m, ProjectChildren.MaxAmount);
        RuleFor(x => x.RelocationCost).InclusiveBetween(0m, ProjectChildren.MaxAmount);
        RuleFor(x => x.IndirectCost).InclusiveBetween(0m, ProjectChildren.MaxAmount);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class AddCostEstimateCommandHandler : IRequestHandler<AddCostEstimateCommand, CostEstimateDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public AddCostEstimateCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<CostEstimateDto> Handle(AddCostEstimateCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Update, ct);
        await ProjectChildren.EnsureProjectAsync(_db, request.ProjectId, ct);

        var latest = await _db.ProjectCostEstimates.Where(e => e.ProjectId == request.ProjectId)
            .MaxAsync(e => (int?)e.Version, ct) ?? 0;
        var estimate = new ProjectCostEstimate
        {
            ProjectId = request.ProjectId,
            Version = latest + 1,
            LandCost = request.LandCost,
            ImprovementsCost = request.ImprovementsCost,
            RelocationCost = request.RelocationCost,
            IndirectCost = request.IndirectCost,
            Notes = ProjectLoading.Clean(request.Notes),
        };
        estimate.RecalculateTotal();
        _db.ProjectCostEstimates.Add(estimate);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) // unique (project, version): two estimates saved at once
        {
            _db.ChangeTracker.Clear();
            throw new ConflictException("estimate_version_conflict", "Another estimate was saved at the same moment. Please try again.");
        }
        return estimate.ToDto();
    }
}

/// <summary>Every version, newest first.</summary>
public sealed record ListCostEstimatesQuery(Guid ProjectId) : IRequest<IReadOnlyList<CostEstimateDto>>;

public sealed class ListCostEstimatesQueryHandler : IRequestHandler<ListCostEstimatesQuery, IReadOnlyList<CostEstimateDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public ListCostEstimatesQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<CostEstimateDto>> Handle(ListCostEstimatesQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Read, ct);
        await ProjectChildren.EnsureProjectAsync(_db, request.ProjectId, ct);
        var rows = await _db.ProjectCostEstimates.Where(e => e.ProjectId == request.ProjectId)
            .OrderByDescending(e => e.Version).ToListAsync(ct);
        return rows.Select(e => e.ToDto()).ToList();
    }
}

// ── Annual budget (ROW-013) ─────────────────────────────────────────────

/// <summary>Sets the ROW budget for a fiscal year, creating it or adjusting the existing amount.</summary>
public sealed record SetProjectBudgetCommand(Guid ProjectId, int FiscalYear, decimal Amount, string? Notes) : IRequest<BudgetDto>;

public sealed class SetProjectBudgetCommandValidator : AbstractValidator<SetProjectBudgetCommand>
{
    public SetProjectBudgetCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.FiscalYear).InclusiveBetween(1900, 2200);
        RuleFor(x => x.Amount).InclusiveBetween(0m, ProjectChildren.MaxAmount);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class SetProjectBudgetCommandHandler : IRequestHandler<SetProjectBudgetCommand, BudgetDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public SetProjectBudgetCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<BudgetDto> Handle(SetProjectBudgetCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Update, ct);
        await ProjectChildren.EnsureProjectAsync(_db, request.ProjectId, ct);

        var budget = await _db.ProjectBudgets
            .FirstOrDefaultAsync(b => b.ProjectId == request.ProjectId && b.FiscalYear == request.FiscalYear, ct);
        if (budget is null)
        {
            budget = new ProjectBudget { ProjectId = request.ProjectId, FiscalYear = request.FiscalYear };
            _db.ProjectBudgets.Add(budget);
        }
        budget.Amount = request.Amount;
        budget.Notes = ProjectLoading.Clean(request.Notes);
        await _db.SaveChangesAsync(ct);
        return budget.ToDto();
    }
}

public sealed record ListProjectBudgetsQuery(Guid ProjectId) : IRequest<IReadOnlyList<BudgetDto>>;

public sealed class ListProjectBudgetsQueryHandler : IRequestHandler<ListProjectBudgetsQuery, IReadOnlyList<BudgetDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public ListProjectBudgetsQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<BudgetDto>> Handle(ListProjectBudgetsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Read, ct);
        await ProjectChildren.EnsureProjectAsync(_db, request.ProjectId, ct);
        var rows = await _db.ProjectBudgets.Where(b => b.ProjectId == request.ProjectId)
            .OrderBy(b => b.FiscalYear).ToListAsync(ct);
        return rows.Select(b => b.ToDto()).ToList();
    }
}

public sealed record DeleteProjectBudgetCommand(Guid ProjectId, int FiscalYear) : IRequest;

public sealed class DeleteProjectBudgetCommandHandler : IRequestHandler<DeleteProjectBudgetCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public DeleteProjectBudgetCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task Handle(DeleteProjectBudgetCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Delete, ct);
        var budget = await _db.ProjectBudgets
            .FirstOrDefaultAsync(b => b.ProjectId == request.ProjectId && b.FiscalYear == request.FiscalYear, ct)
            ?? throw new NotFoundException("Budget", $"{request.ProjectId}/{request.FiscalYear}");
        _db.ProjectBudgets.Remove(budget);
        await _db.SaveChangesAsync(ct);
    }
}

// ── ROW Authorizations (ROW-238) ────────────────────────────────────────

public interface IAuthorizationFields
{
    AuthorizationType Type { get; }
    decimal AuthorizedAmount { get; }
    decimal? FederalSharePercent { get; }
    string? ReferenceNumber { get; }
    string? Notes { get; }
}

public abstract class AuthorizationFieldsValidator<T> : AbstractValidator<T> where T : IAuthorizationFields
{
    protected AuthorizationFieldsValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.AuthorizedAmount).InclusiveBetween(0m, ProjectChildren.MaxAmount);
        RuleFor(x => x.FederalSharePercent).InclusiveBetween(0m, 100m).When(x => x.FederalSharePercent is not null);
        RuleFor(x => x.ReferenceNumber).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed record AddRowAuthorizationCommand(
    Guid ProjectId, AuthorizationType Type, decimal AuthorizedAmount, decimal? FederalSharePercent,
    DateOnly AuthorizedOn, string? ReferenceNumber, bool ProgrammingConfirmed, string? Notes)
    : IRequest<RowAuthorizationDto>, IAuthorizationFields;

public sealed class AddRowAuthorizationCommandValidator : AuthorizationFieldsValidator<AddRowAuthorizationCommand>
{
    public AddRowAuthorizationCommandValidator() => RuleFor(x => x.ProjectId).NotEmpty();
}

public sealed class AddRowAuthorizationCommandHandler : IRequestHandler<AddRowAuthorizationCommand, RowAuthorizationDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public AddRowAuthorizationCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<RowAuthorizationDto> Handle(AddRowAuthorizationCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Update, ct);
        await ProjectChildren.EnsureProjectAsync(_db, request.ProjectId, ct);

        var auth = new RowAuthorization
        {
            ProjectId = request.ProjectId,
            Type = request.Type,
            AuthorizedAmount = request.AuthorizedAmount,
            FederalSharePercent = request.FederalSharePercent,
            AuthorizedOn = request.AuthorizedOn,
            ReferenceNumber = ProjectLoading.Clean(request.ReferenceNumber),
            ProgrammingConfirmed = request.ProgrammingConfirmed,
            Notes = ProjectLoading.Clean(request.Notes),
        };
        _db.RowAuthorizations.Add(auth);
        await _db.SaveChangesAsync(ct);
        return auth.ToDto();
    }
}

public sealed record UpdateRowAuthorizationCommand(
    Guid ProjectId, Guid Id, AuthorizationType Type, decimal AuthorizedAmount, decimal? FederalSharePercent,
    DateOnly AuthorizedOn, string? ReferenceNumber, bool ProgrammingConfirmed, string? Notes)
    : IRequest<RowAuthorizationDto>, IAuthorizationFields;

public sealed class UpdateRowAuthorizationCommandValidator : AuthorizationFieldsValidator<UpdateRowAuthorizationCommand>
{
    public UpdateRowAuthorizationCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class UpdateRowAuthorizationCommandHandler : IRequestHandler<UpdateRowAuthorizationCommand, RowAuthorizationDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public UpdateRowAuthorizationCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<RowAuthorizationDto> Handle(UpdateRowAuthorizationCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Update, ct);
        var auth = await _db.RowAuthorizations.FirstOrDefaultAsync(a => a.Id == request.Id && a.ProjectId == request.ProjectId, ct)
                   ?? throw new NotFoundException("ROW authorization", request.Id);

        auth.Type = request.Type;
        auth.AuthorizedAmount = request.AuthorizedAmount;
        auth.FederalSharePercent = request.FederalSharePercent;
        auth.AuthorizedOn = request.AuthorizedOn;
        auth.ReferenceNumber = ProjectLoading.Clean(request.ReferenceNumber);
        auth.ProgrammingConfirmed = request.ProgrammingConfirmed;
        auth.Notes = ProjectLoading.Clean(request.Notes);
        await _db.SaveChangesAsync(ct);
        return auth.ToDto();
    }
}

public sealed record DeleteRowAuthorizationCommand(Guid ProjectId, Guid Id) : IRequest;

public sealed class DeleteRowAuthorizationCommandHandler : IRequestHandler<DeleteRowAuthorizationCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public DeleteRowAuthorizationCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task Handle(DeleteRowAuthorizationCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Delete, ct);
        var auth = await _db.RowAuthorizations.FirstOrDefaultAsync(a => a.Id == request.Id && a.ProjectId == request.ProjectId, ct)
                   ?? throw new NotFoundException("ROW authorization", request.Id);
        _db.RowAuthorizations.Remove(auth);
        await _db.SaveChangesAsync(ct);
    }
}

public sealed record ListRowAuthorizationsQuery(Guid ProjectId) : IRequest<IReadOnlyList<RowAuthorizationDto>>;

public sealed class ListRowAuthorizationsQueryHandler : IRequestHandler<ListRowAuthorizationsQuery, IReadOnlyList<RowAuthorizationDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public ListRowAuthorizationsQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<RowAuthorizationDto>> Handle(ListRowAuthorizationsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Read, ct);
        await ProjectChildren.EnsureProjectAsync(_db, request.ProjectId, ct);
        var rows = await _db.RowAuthorizations.Where(a => a.ProjectId == request.ProjectId)
            .OrderBy(a => a.AuthorizedOn).ThenBy(a => a.Type).ToListAsync(ct);
        return rows.Select(a => a.ToDto()).ToList();
    }
}
