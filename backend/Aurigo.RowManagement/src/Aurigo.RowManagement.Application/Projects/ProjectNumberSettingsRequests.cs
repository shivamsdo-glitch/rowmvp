using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Projects;

/// <summary>The tenant's project-number format and fiscal-year start, with an example of the first number it produces.</summary>
public sealed record GetProjectNumberSettingsQuery : IRequest<ProjectNumberSettingsDto>;

public sealed class GetProjectNumberSettingsQueryHandler : IRequestHandler<GetProjectNumberSettingsQuery, ProjectNumberSettingsDto>
{
    private readonly ProjectNumberGenerator _numbers;
    private readonly IPagePermissionService _permissions;

    public GetProjectNumberSettingsQueryHandler(ProjectNumberGenerator numbers, IPagePermissionService permissions)
    {
        _numbers = numbers;
        _permissions = permissions;
    }

    public async Task<ProjectNumberSettingsDto> Handle(GetProjectNumberSettingsQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Read, ct);
        var f = await _numbers.GetFormatAsync(ct);
        return new ProjectNumberSettingsDto(f.Pattern, f.FiscalYearStartMonth, _numbers.Example(f));
    }
}

/// <summary>Changing the pattern affects only projects created afterwards; existing numbers are never rewritten.</summary>
public sealed record SetProjectNumberSettingsCommand(string Pattern, int FiscalYearStartMonth) : IRequest<ProjectNumberSettingsDto>;

public sealed class SetProjectNumberSettingsCommandValidator : AbstractValidator<SetProjectNumberSettingsCommand>
{
    public SetProjectNumberSettingsCommandValidator()
    {
        RuleFor(x => x.Pattern).Custom((pattern, ctx) =>
        {
            foreach (var problem in ProjectNumbering.Validate(pattern))
                ctx.AddFailure(problem);
        });
        RuleFor(x => x.FiscalYearStartMonth).InclusiveBetween(1, 12);
    }
}

public sealed class SetProjectNumberSettingsCommandHandler : IRequestHandler<SetProjectNumberSettingsCommand, ProjectNumberSettingsDto>
{
    private readonly IRowDbContext _db;
    private readonly ProjectNumberGenerator _numbers;
    private readonly IPagePermissionService _permissions;

    public SetProjectNumberSettingsCommandHandler(IRowDbContext db, ProjectNumberGenerator numbers, IPagePermissionService permissions)
    {
        _db = db;
        _numbers = numbers;
        _permissions = permissions;
    }

    public async Task<ProjectNumberSettingsDto> Handle(SetProjectNumberSettingsCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);

        var settings = await _db.ProjectNumberSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new ProjectNumberSettings();
            _db.ProjectNumberSettings.Add(settings);
        }
        settings.Pattern = request.Pattern.Trim();
        settings.FiscalYearStartMonth = request.FiscalYearStartMonth;
        await _db.SaveChangesAsync(ct);

        var f = new ProjectNumberFormat(settings.Pattern, settings.FiscalYearStartMonth);
        return new ProjectNumberSettingsDto(f.Pattern, f.FiscalYearStartMonth, _numbers.Example(f));
    }
}
