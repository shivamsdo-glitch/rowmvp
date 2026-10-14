using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Mappers;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using FluentValidation;
using MediatR;

namespace Aurigo.RowManagement.Application.Projects;

/// <summary>
/// Gives a project its own time zone (an agency can span more than one), or, with a null id, makes it follow the agency's.
/// Its parcels count deadlines and "today" on that calendar.
/// </summary>
public sealed record SetProjectTimeZoneCommand(Guid ProjectId, string? TimeZoneId) : IRequest<ProjectDto>;

public sealed class SetProjectTimeZoneCommandValidator : AbstractValidator<SetProjectTimeZoneCommand>
{
    public SetProjectTimeZoneCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.TimeZoneId).Must(AgencyTime.IsSupported).When(x => x.TimeZoneId is not null).WithMessage("Choose one of the listed time zones.");
    }
}

public sealed class SetProjectTimeZoneCommandHandler : IRequestHandler<SetProjectTimeZoneCommand, ProjectDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;

    public SetProjectTimeZoneCommandHandler(IRowDbContext db, IPagePermissionService permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async Task<ProjectDto> Handle(SetProjectTimeZoneCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Update, ct);
        var project = await ProjectLoading.LoadAsync(_db, request.ProjectId, ct);
        project.TimeZoneId = request.TimeZoneId;
        await _db.SaveChangesAsync(ct);
        return project.ToDto();
    }
}
