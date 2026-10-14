using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Agency;

public sealed record TimeZoneOptionDto(string Id, string Label);

/// <summary>The agency's default time zone, the zones it can choose from, and today's date there.</summary>
public sealed record AgencyTimeZoneDto(string TimeZoneId, IReadOnlyList<TimeZoneOptionDto> Options, DateOnly Today);

internal static class AgencyTimeZoneDtos
{
    public static async Task<AgencyTimeZoneDto> BuildAsync(AgencyClock clock, CancellationToken ct)
    {
        var zone = await clock.AgencyZoneAsync(ct);
        return new AgencyTimeZoneDto(zone, AgencyTime.Supported.Select(z => new TimeZoneOptionDto(z.Id, z.Label)).ToList(), AgencyTime.TodayIn(zone, clock.UtcNow));
    }
}

/// <summary>Anyone who can see projects can see the agency's time zone (the project screens need the choices).</summary>
public sealed record GetAgencyTimeZoneQuery : IRequest<AgencyTimeZoneDto>;

public sealed class GetAgencyTimeZoneQueryHandler : IRequestHandler<GetAgencyTimeZoneQuery, AgencyTimeZoneDto>
{
    private readonly AgencyClock _clock;
    private readonly IPagePermissionService _permissions;

    public GetAgencyTimeZoneQueryHandler(AgencyClock clock, IPagePermissionService permissions)
    {
        _clock = clock;
        _permissions = permissions;
    }

    public async Task<AgencyTimeZoneDto> Handle(GetAgencyTimeZoneQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Projects, PermissionAction.Read, ct);
        return await AgencyTimeZoneDtos.BuildAsync(_clock, ct);
    }
}

/// <summary>Sets the time zone the agency counts days in. Projects without their own zone follow it.</summary>
public sealed record SetAgencyTimeZoneCommand(string TimeZoneId) : IRequest<AgencyTimeZoneDto>;

public sealed class SetAgencyTimeZoneCommandValidator : AbstractValidator<SetAgencyTimeZoneCommand>
{
    public SetAgencyTimeZoneCommandValidator() =>
        RuleFor(x => x.TimeZoneId).Must(AgencyTime.IsSupported).WithMessage("Choose one of the listed time zones.");
}

public sealed class SetAgencyTimeZoneCommandHandler : IRequestHandler<SetAgencyTimeZoneCommand, AgencyTimeZoneDto>
{
    private readonly IRowDbContext _db;
    private readonly AgencyClock _clock;
    private readonly IPagePermissionService _permissions;

    public SetAgencyTimeZoneCommandHandler(IRowDbContext db, AgencyClock clock, IPagePermissionService permissions)
    {
        _db = db;
        _clock = clock;
        _permissions = permissions;
    }

    public async Task<AgencyTimeZoneDto> Handle(SetAgencyTimeZoneCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);
        var settings = await _db.AgencySettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new AgencySettings();
            _db.AgencySettings.Add(settings);
        }
        settings.TimeZoneId = request.TimeZoneId;
        await _db.SaveChangesAsync(ct);
        return await AgencyTimeZoneDtos.BuildAsync(_clock, ct);
    }
}
