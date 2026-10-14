using Aurigo.RowManagement.Application.Mappers;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Errors;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Auth;

/// <summary>The authenticated caller, from the user row when one exists, otherwise from the token claims.</summary>
public sealed record GetCurrentUserQuery : IRequest<UserDto>;

public sealed class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, UserDto>
{
    private readonly IRowDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ICurrentTenant _tenant;

    public GetCurrentUserQueryHandler(IRowDbContext db, ICurrentUser user, ICurrentTenant tenant)
    {
        _db = db;
        _user = user;
        _tenant = tenant;
    }

    public async Task<UserDto> Handle(GetCurrentUserQuery request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated)
            throw new AuthenticationFailedException("unauthenticated", "Authentication is required.");

        // Tenant-filtered: a caller can only ever resolve a user inside their own tenant.
        var user = await _db.AppUsers
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == _user.Id, ct);

        var tenantName = _tenant.IsAvailable
            ? (await _db.Tenants.FirstOrDefaultAsync(t => t.Id == _tenant.Value, ct))?.Name
            : null;

        if (user is not null)
            return user.ToDto(tenantName);

        // Token not backed by a user row (e.g. a dev token): describe the caller from claims.
        return new UserDto(
            _user.Id, "", "", _user.Email,
            UserMapper.Initials(null, null, _user.Email),
            _user.Email, _user.Roles, _tenant.Value, tenantName);
    }
}
