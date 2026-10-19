using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.ExternalServices;
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

/// <summary>The editable fields of a project, shared by create and update so they validate the same way.</summary>
public interface IProjectFields
{
    string? Number { get; }
    string Name { get; }
    string? Description { get; }
    string? Region { get; }
    string? District { get; }
    OversightType OversightType { get; }
    string? FundingSource { get; }
    decimal? FundingAmount { get; }
    decimal? DesignEstimate { get; }
    DateOnly? LettingDate { get; }
    DateOnly? PlanTurnInDate { get; }
    DateOnly? ConstructionStartDate { get; }
    DateOnly? ConstructionEndDate { get; }
    string? Phase { get; }
    string? Location { get; }
    Guid? ProjectManagerUserId { get; }
    string? DocumentFolderStructure { get; }
    string? DocumentProperties { get; }
    string? ConstructionProjectManager { get; }
    DateOnly? RightOfWayClearDate { get; }
    string? LinkedSource { get; }
    string? LinkedProjectNumber { get; }
}

public abstract class ProjectFieldsValidator<T> : AbstractValidator<T> where T : IProjectFields
{
    protected ProjectFieldsValidator()
    {
        RuleFor(x => x.Number).MaximumLength(ProjectNumbering.MaxLength)
            .Matches(@"^[A-Za-z0-9\-_/.]+$").When(x => !string.IsNullOrWhiteSpace(x.Number))
            .WithMessage("Project number may contain only letters, digits and - _ / .");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.").MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Region).MaximumLength(100);
        RuleFor(x => x.District).MaximumLength(100);
        RuleFor(x => x.OversightType).IsInEnum();
        RuleFor(x => x.FundingSource).MaximumLength(200);
        RuleFor(x => x.FundingAmount).GreaterThanOrEqualTo(0m).LessThan(1_000_000_000_000m);
        RuleFor(x => x.DesignEstimate).GreaterThanOrEqualTo(0m).LessThan(1_000_000_000_000m);
        RuleFor(x => x.Phase).MaximumLength(100);
        RuleFor(x => x.Location).MaximumLength(500);
        RuleFor(x => x.DocumentFolderStructure).MaximumLength(100);
        RuleFor(x => x.DocumentProperties).MaximumLength(100);
        RuleFor(x => x.ConstructionProjectManager).MaximumLength(200);
        RuleFor(x => x.LinkedSource).Must(CatalogSources.IsKnown).When(x => !string.IsNullOrWhiteSpace(x.LinkedSource))
            .WithMessage("Link to a Planned or a Build project.");
        RuleFor(x => x.LinkedProjectNumber).NotEmpty().MaximumLength(50).When(x => !string.IsNullOrWhiteSpace(x.LinkedSource))
            .WithMessage("Choose the project to link to.");
        RuleFor(x => x.LinkedSource).NotEmpty().When(x => !string.IsNullOrWhiteSpace(x.LinkedProjectNumber))
            .WithMessage("Say whether the linked project is Planned or Build.");
        RuleFor(x => x.ConstructionEndDate)
            .GreaterThanOrEqualTo(x => x.ConstructionStartDate!.Value)
            .When(x => x.ConstructionStartDate is not null && x.ConstructionEndDate is not null)
            .WithMessage("Construction end cannot be before construction start.");
    }
}

internal static class ProjectLoading
{
    public static async Task<Project> LoadAsync(IRowDbContext db, Guid id, CancellationToken ct) =>
        await db.Projects.Include(p => p.Estimates).Include(p => p.Authorizations).Include(p => p.Budgets)
            .FirstOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new NotFoundException("Project", id);

    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static async Task EnsureNumberFreeAsync(IRowDbContext db, string number, Guid? exceptId, CancellationToken ct)
    {
        var lower = number.ToLower();
        if (await db.Projects.AnyAsync(p => p.Number.ToLower() == lower && p.Id != exceptId, ct))
            throw new ConflictException("project_number_exists", $"A project numbered '{number}' already exists.");
    }

    public static void Apply(Project p, IProjectFields f)
    {
        p.Name = f.Name.Trim();
        p.Description = Clean(f.Description);
        p.Region = Clean(f.Region);
        p.District = Clean(f.District);
        p.OversightType = f.OversightType;
        p.FundingSource = Clean(f.FundingSource);
        p.FundingAmount = f.FundingAmount;
        p.DesignEstimate = f.DesignEstimate;
        p.LettingDate = f.LettingDate;
        p.PlanTurnInDate = f.PlanTurnInDate;
        p.ConstructionStartDate = f.ConstructionStartDate;
        p.ConstructionEndDate = f.ConstructionEndDate;
        p.Phase = Clean(f.Phase);
        p.Location = Clean(f.Location);
        p.DocumentFolderStructure = Clean(f.DocumentFolderStructure);
        p.DocumentProperties = Clean(f.DocumentProperties);
        p.ConstructionProjectManager = Clean(f.ConstructionProjectManager);
        p.RightOfWayClearDate = f.RightOfWayClearDate;
    }

    /// <summary>
    /// The fields that need another record to be true: the project manager must be an active person in this agency (the name is kept as
    /// chosen) and a linked Planned or Build project must exist in that module.
    /// </summary>
    public static async Task ApplyLookupsAsync(IRowDbContext db, IProjectCatalogClient? catalog, Project p, IProjectFields f, CancellationToken ct)
    {
        if (f.ProjectManagerUserId is { } managerId)
        {
            if (p.ProjectManagerUserId != managerId)
            {
                var user = await db.AppUsers.FirstOrDefaultAsync(u => u.Id == managerId && u.IsActive, ct)
                    ?? throw new InvalidRequestException("unknown_project_manager", "Choose a project manager from the list.");
                p.ProjectManagerName = user.FullName;
            }
            p.ProjectManagerUserId = managerId;
        }
        else
        {
            p.ProjectManagerUserId = null;
            p.ProjectManagerName = null;
        }

        if (string.IsNullOrWhiteSpace(f.LinkedSource))
        {
            p.LinkedSource = null;
            p.LinkedProjectNumber = null;
            p.LinkedProjectName = null;
        }
        else if (p.LinkedSource != f.LinkedSource || !string.Equals(p.LinkedProjectNumber, f.LinkedProjectNumber?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            var found = catalog is null ? null : await catalog.FindAsync(f.LinkedSource!, f.LinkedProjectNumber!, ct);
            if (found is null)
                throw new InvalidRequestException("unknown_linked_project", $"There is no {f.LinkedSource} project numbered '{f.LinkedProjectNumber!.Trim()}'.");
            p.LinkedSource = found.Source;
            p.LinkedProjectNumber = found.Number;
            p.LinkedProjectName = found.Name;
        }
    }
}

// ── Create (ROW-001, 003, 004) ──────────────────────────────────────────

/// <summary>Leave <see cref="Number"/> empty to have one generated from the tenant's format.</summary>
public sealed record CreateProjectCommand(
    string? Number, string Name, string? Description, string? Region, string? District, OversightType OversightType,
    string? FundingSource, decimal? FundingAmount, decimal? DesignEstimate,
    DateOnly? LettingDate, DateOnly? PlanTurnInDate, DateOnly? ConstructionStartDate, DateOnly? ConstructionEndDate,
    string? Phase = null, string? Location = null, Guid? ProjectManagerUserId = null, string? DocumentFolderStructure = null,
    string? DocumentProperties = null, string? ConstructionProjectManager = null, DateOnly? RightOfWayClearDate = null,
    string? LinkedSource = null, string? LinkedProjectNumber = null)
    : IRequest<ProjectDto>, IProjectFields;

public sealed class CreateProjectCommandValidator : ProjectFieldsValidator<CreateProjectCommand> { }

public sealed class CreateProjectCommandHandler : IRequestHandler<CreateProjectCommand, ProjectDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ProjectNumberGenerator _numbers;
    private readonly ICurrentTenant _tenant;
    private readonly IProjectCatalogClient? _catalog;

    /// <summary>
    /// One issuer at a time per agency within this server, so simultaneous creates queue instead of fighting over the
    /// counter row. The counter's concurrency token and the retry below still protect against several servers.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> IssueLocks = new();

    public CreateProjectCommandHandler(
        IRowDbContext db, IPagePermissionService permissions, ProjectNumberGenerator numbers, ICurrentTenant tenant,
        IProjectCatalogClient? catalog = null)
    {
        _db = db;
        _permissions = permissions;
        _numbers = numbers;
        _tenant = tenant;
        _catalog = catalog;
    }

    public async Task<ProjectDto> Handle(CreateProjectCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Create, ct);

        if (!string.IsNullOrWhiteSpace(request.Number))
        {
            var typed = request.Number.Trim();
            await ProjectLoading.EnsureNumberFreeAsync(_db, typed, null, ct);
            var project = await TrySaveAsync(typed, request, ct)
                          ?? throw new ConflictException("project_number_exists", $"A project numbered '{typed}' already exists.");
            return project.ToDto();
        }

        // A generated number can lose a race with another create (same counter value). Nothing was saved, so
        // start again from fresh state; the winner's counter is then visible and the next number is free.
        var gate = IssueLocks.GetOrAdd(_tenant.IsAvailable ? _tenant.Value : Guid.Empty, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= MaxGenerateAttempts; attempt++)
            {
                var saved = await TrySaveAsync(await _numbers.NextAsync(ct), request, ct);
                if (saved is not null) return saved.ToDto();
                await Task.Delay(Random.Shared.Next(5, 25 * attempt), ct);
            }
        }
        finally
        {
            gate.Release();
        }
        throw new ConflictException("project_number_conflict", "The system is busy issuing project numbers. Please try again.");
    }

    private const int MaxGenerateAttempts = 8;

    /// <summary>Saves the project, or returns null (with the unsaved changes discarded) when the number or counter was taken concurrently.</summary>
    private async Task<Project?> TrySaveAsync(string number, IProjectFields fields, CancellationToken ct)
    {
        var project = new Project { Number = number };
        ProjectLoading.Apply(project, fields);
        await ProjectLoading.ApplyLookupsAsync(_db, _catalog, project, fields, ct);
        _db.Projects.Add(project);
        try
        {
            await _db.SaveChangesAsync(ct);
            return project;
        }
        catch (DbUpdateException) // includes DbUpdateConcurrencyException
        {
            _db.ChangeTracker.Clear();
            return null;
        }
    }
}

// ── Update ──────────────────────────────────────────────────────────────

public sealed record UpdateProjectCommand(
    Guid Id, string? Number, string Name, string? Description, string? Region, string? District, OversightType OversightType,
    string? FundingSource, decimal? FundingAmount, decimal? DesignEstimate,
    DateOnly? LettingDate, DateOnly? PlanTurnInDate, DateOnly? ConstructionStartDate, DateOnly? ConstructionEndDate,
    string? Phase = null, string? Location = null, Guid? ProjectManagerUserId = null, string? DocumentFolderStructure = null,
    string? DocumentProperties = null, string? ConstructionProjectManager = null, DateOnly? RightOfWayClearDate = null,
    string? LinkedSource = null, string? LinkedProjectNumber = null)
    : IRequest<ProjectDto>, IProjectFields;

public sealed class UpdateProjectCommandValidator : ProjectFieldsValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class UpdateProjectCommandHandler : IRequestHandler<UpdateProjectCommand, ProjectDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    private readonly IProjectCatalogClient? _catalog;

    public UpdateProjectCommandHandler(IRowDbContext db, IPagePermissionService permissions, IProjectCatalogClient? catalog = null)
    {
        _db = db;
        _permissions = permissions;
        _catalog = catalog;
    }

    public async Task<ProjectDto> Handle(UpdateProjectCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Update, ct);
        var project = await ProjectLoading.LoadAsync(_db, request.Id, ct);

        // A blank number keeps the current one; a different number is an explicit edit and must stay unique.
        var number = string.IsNullOrWhiteSpace(request.Number) ? project.Number : request.Number.Trim();
        if (!string.Equals(number, project.Number, StringComparison.OrdinalIgnoreCase))
            await ProjectLoading.EnsureNumberFreeAsync(_db, number, project.Id, ct);

        project.Number = number;
        ProjectLoading.Apply(project, request);
        await ProjectLoading.ApplyLookupsAsync(_db, _catalog, project, request, ct);
        await _db.SaveChangesAsync(ct);
        return project.ToDto();
    }
}

// ── Get / List ──────────────────────────────────────────────────────────

public sealed record GetProjectQuery(Guid Id) : IRequest<ProjectDto>;

public sealed class GetProjectQueryHandler : IRequestHandler<GetProjectQuery, ProjectDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public GetProjectQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<ProjectDto> Handle(GetProjectQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Read, ct);
        return (await ProjectLoading.LoadAsync(_db, request.Id, ct)).ToDto();
    }
}

public sealed record ListProjectsQuery(
    string? Search, string? Region, string? District, OversightType? OversightType, int Page = 1, int PageSize = 25)
    : IRequest<PagedResult<ProjectSummaryDto>>;

public sealed class ListProjectsQueryValidator : AbstractValidator<ListProjectsQuery>
{
    public ListProjectsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public sealed class ListProjectsQueryHandler : IRequestHandler<ListProjectsQuery, PagedResult<ProjectSummaryDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public ListProjectsQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<PagedResult<ProjectSummaryDto>> Handle(ListProjectsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Read, ct);

        var query = _db.Projects.AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(p => p.Number.ToLower().Contains(term) || p.Name.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(request.Region)) query = query.Where(p => p.Region == request.Region);
        if (!string.IsNullOrWhiteSpace(request.District)) query = query.Where(p => p.District == request.District);
        if (request.OversightType is { } o) query = query.Where(p => p.OversightType == o);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(p => p.Number)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(p => new
            {
                p.Id, p.Number, p.Name, p.Region, p.District, p.OversightType, p.NepaDecisionType,
                p.ExceptionType, p.ExceptionApprovedOn,
                HasAcquisitionAuthorization = p.Authorizations.Any(a => a.Type != AuthorizationType.Preliminary),
                CurrentEstimate = p.Estimates.OrderByDescending(e => e.Version).Select(e => (decimal?)e.Total).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new ProjectSummaryDto(
            r.Id, r.Number, r.Name, r.Region, r.District, r.OversightType, r.CurrentEstimate,
            ProjectMapper.EvaluateGate(r.OversightType, r.NepaDecisionType, r.HasAcquisitionAuthorization,
                r.ExceptionType, r.ExceptionApprovedOn).ToDto())).ToList();
        return new PagedResult<ProjectSummaryDto>(items, total, request.Page, request.PageSize);
    }
}

// ── NEPA decision (ROW-076) and acquisition exception ───────────────────

/// <summary>Records or, with a null type, clears the NEPA decision.</summary>
public sealed record RecordNepaDecisionCommand(Guid ProjectId, NepaDecisionType? DecisionType, DateOnly? DecisionDate)
    : IRequest<ProjectDto>;

public sealed class RecordNepaDecisionCommandValidator : AbstractValidator<RecordNepaDecisionCommand>
{
    public RecordNepaDecisionCommandValidator()
    {
        RuleFor(x => x.DecisionType).IsInEnum().When(x => x.DecisionType is not null);
        RuleFor(x => x.DecisionDate).NotNull().When(x => x.DecisionType is not null)
            .WithMessage("A decision date is required with a NEPA decision.");
        RuleFor(x => x.DecisionDate).Null().When(x => x.DecisionType is null)
            .WithMessage("Remove the date when clearing the decision.");
    }
}

public sealed class RecordNepaDecisionCommandHandler : IRequestHandler<RecordNepaDecisionCommand, ProjectDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public RecordNepaDecisionCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<ProjectDto> Handle(RecordNepaDecisionCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Update, ct);
        var project = await ProjectLoading.LoadAsync(_db, request.ProjectId, ct);
        project.NepaDecisionType = request.DecisionType;
        project.NepaDecisionDate = request.DecisionDate;
        await _db.SaveChangesAsync(ct);
        return project.ToDto();
    }
}

/// <summary>Approves (or, with a null type, withdraws) an early, hardship or protective acquisition (Diagram 1, GT_c).</summary>
public sealed record SetAcquisitionExceptionCommand(
    Guid ProjectId, AcquisitionExceptionType? ExceptionType, DateOnly? ApprovedOn, string? Reason) : IRequest<ProjectDto>;

public sealed class SetAcquisitionExceptionCommandValidator : AbstractValidator<SetAcquisitionExceptionCommand>
{
    public SetAcquisitionExceptionCommandValidator()
    {
        RuleFor(x => x.ExceptionType).IsInEnum().When(x => x.ExceptionType is not null);
        RuleFor(x => x.ApprovedOn).NotNull().When(x => x.ExceptionType is not null)
            .WithMessage("An approval date is required.");
        RuleFor(x => x.Reason).NotEmpty().When(x => x.ExceptionType is not null)
            .WithMessage("A reason is required for an exception.");
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public sealed class SetAcquisitionExceptionCommandHandler : IRequestHandler<SetAcquisitionExceptionCommand, ProjectDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public SetAcquisitionExceptionCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<ProjectDto> Handle(SetAcquisitionExceptionCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Update, ct);
        var project = await ProjectLoading.LoadAsync(_db, request.ProjectId, ct);
        var cleared = request.ExceptionType is null;
        project.ExceptionType = request.ExceptionType;
        project.ExceptionApprovedOn = cleared ? null : request.ApprovedOn;
        project.ExceptionReason = cleared ? null : ProjectLoading.Clean(request.Reason);
        await _db.SaveChangesAsync(ct);
        return project.ToDto();
    }
}

// ── Acquisition gate ────────────────────────────────────────────────────

public sealed record GetAcquisitionGateQuery(Guid ProjectId) : IRequest<AcquisitionGateDto>;

public sealed class GetAcquisitionGateQueryHandler : IRequestHandler<GetAcquisitionGateQuery, AcquisitionGateDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public GetAcquisitionGateQueryHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<AcquisitionGateDto> Handle(GetAcquisitionGateQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Read, ct);
        return (await ProjectLoading.LoadAsync(_db, request.ProjectId, ct)).EvaluateGate().ToDto();
    }
}

// ── Import (ROW-001: number "from another system") ─────────────────────

public sealed record ProjectImportRow(
    string? Number, string Name, string? Description, string? Region, string? District, OversightType OversightType,
    string? FundingSource, decimal? FundingAmount, decimal? DesignEstimate,
    DateOnly? LettingDate, DateOnly? PlanTurnInDate, DateOnly? ConstructionStartDate, DateOnly? ConstructionEndDate);

/// <summary>Creates each row as its own project; a bad row is reported and the rest still go in.</summary>
public sealed record ImportProjectsCommand(IReadOnlyList<ProjectImportRow> Rows) : IRequest<ProjectImportSummaryDto>;

public sealed class ImportProjectsCommandValidator : AbstractValidator<ImportProjectsCommand>
{
    public const int MaxRows = 500;

    public ImportProjectsCommandValidator() =>
        RuleFor(x => x.Rows).NotEmpty().Must(r => r.Count <= MaxRows).WithMessage($"Import at most {MaxRows} rows at a time.");
}

public sealed class ImportProjectsCommandHandler : IRequestHandler<ImportProjectsCommand, ProjectImportSummaryDto>
{
    private readonly ISender _sender;
    private readonly IRowDbContext _db;

    public ImportProjectsCommandHandler(ISender sender, IRowDbContext db)
    {
        _sender = sender;
        _db = db;
    }

    public async Task<ProjectImportSummaryDto> Handle(ImportProjectsCommand request, CancellationToken ct)
    {
        var results = new List<ProjectImportResultDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowNo = 0;
        foreach (var r in request.Rows)
        {
            rowNo++;
            if (!string.IsNullOrWhiteSpace(r.Number) && !seen.Add(r.Number.Trim()))
            {
                results.Add(new(rowNo, "Failed", null, r.Number, $"Number '{r.Number}' appears more than once in this file."));
                continue;
            }
            try
            {
                var created = await _sender.Send(new CreateProjectCommand(
                    r.Number, r.Name, r.Description, r.Region, r.District, r.OversightType, r.FundingSource,
                    r.FundingAmount, r.DesignEstimate, r.LettingDate, r.PlanTurnInDate,
                    r.ConstructionStartDate, r.ConstructionEndDate), ct);
                results.Add(new(rowNo, "Created", created.Id, created.Number, null));
            }
            catch (ValidationException ex)
            {
                _db.ChangeTracker.Clear();
                results.Add(new(rowNo, "Failed", null, r.Number, string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct())));
            }
            catch (DomainException ex) when (ex is not ForbiddenException)
            {
                _db.ChangeTracker.Clear();
                results.Add(new(rowNo, "Failed", null, r.Number, ex.Message));
            }
        }
        return new ProjectImportSummaryDto(
            results.Count(x => x.Outcome == "Created"), results.Count(x => x.Outcome == "Failed"), results);
    }
}
