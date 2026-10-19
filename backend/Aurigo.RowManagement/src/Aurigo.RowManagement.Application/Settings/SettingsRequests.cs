using System.Text.RegularExpressions;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.Persistence;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.Application.Settings;

// Phase 1.5 slice 2 (ADR 0014): Settings > Users & Roles and Settings > Permissions. Everything here is governed by the
// Administration page permission. Roles and their page grants are per agency data; the default roles are "system" roles.

// ── DTOs ────────────────────────────────────────────────────────────────

public sealed record RolePermissionDto(string PageKey, bool CanCreate, bool CanRead, bool CanUpdate, bool CanDelete);

public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, bool IsLocked, int UserCount, IReadOnlyList<RolePermissionDto> Permissions);

public sealed record SettingsUserDto(Guid Id, string Username, string FirstName, string LastName, string Email, bool IsActive, Guid? RoleId, string? RoleName);

// ── Shared rules ────────────────────────────────────────────────────────

internal static class SettingsRules
{
    public const int MaxRoleName = 60;
    public static readonly Regex RoleNamePattern = new(@"^[A-Za-z0-9][A-Za-z0-9 &/\-]*$", RegexOptions.Compiled);
    public static readonly Regex UsernamePattern = new(@"^[a-z0-9][a-z0-9._\-]*$", RegexOptions.Compiled);

    /// <summary>The Admin role keeps full access and cannot be renamed, edited or removed: someone must always be able to fix access.</summary>
    public static bool IsLocked(Role r) => r.Name == RoleNames.Admin;

    public static string NormalizeUsername(string value) => value.Trim().ToLowerInvariant();

    public static async Task<Role> LoadRoleAsync(IRowDbContext db, Guid id, CancellationToken ct) =>
        await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Role", id);

    public static RoleDto ToDto(Role r, int userCount) => new(
        r.Id, r.Name, r.Description, r.IsSystem, IsLocked(r), userCount,
        PageKeys.All.Select(p =>
        {
            var row = r.Permissions.FirstOrDefault(x => x.PageKey == p);
            return new RolePermissionDto(p, row?.CanCreate ?? false, row?.CanRead ?? false, row?.CanUpdate ?? false, row?.CanDelete ?? false);
        }).ToList());

    public static async Task<RoleDto> ToDtoAsync(IRowDbContext db, Role r, CancellationToken ct) =>
        ToDto(r, await db.UserRoles.CountAsync(ur => ur.RoleId == r.Id, ct));

    public static SettingsUserDto ToDto(AppUser u) => new(
        u.Id, u.Username, u.FirstName, u.LastName, u.Email, u.IsActive,
        u.UserRoles.Select(ur => (Guid?)ur.RoleId).FirstOrDefault(),
        u.UserRoles.Select(ur => ur.Role?.Name).FirstOrDefault());

    public static void AddEmptyPermissions(Role role)
    {
        foreach (var page in PageKeys.All)
            role.Permissions.Add(new RolePagePermission { TenantId = role.TenantId, RoleId = role.Id, PageKey = page, Template = PermissionTemplates.Custom });
    }

    /// <summary>At least one other active person must keep the Admin role, so access can always be repaired.</summary>
    public static async Task EnsureAnotherAdminAsync(IRowDbContext db, Guid exceptUserId, CancellationToken ct)
    {
        var others = await db.UserRoles.CountAsync(ur => ur.Role!.Name == RoleNames.Admin && ur.UserId != exceptUserId && ur.User!.IsActive && !ur.User.IsLocked, ct);
        if (others == 0)
            throw new ConflictException("last_admin", "This is the last active administrator. Make someone else an administrator first.");
    }
}

// ── Roles: reading ──────────────────────────────────────────────────────

public sealed record ListRolesQuery : IRequest<IReadOnlyList<RoleDto>>;

public sealed class ListRolesQueryHandler : IRequestHandler<ListRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    public ListRolesQueryHandler(IRowDbContext db, IPagePermissionService permissions) { _db = db; _permissions = permissions; }

    public async Task<IReadOnlyList<RoleDto>> Handle(ListRolesQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Read, ct);
        var roles = await _db.Roles.Include(r => r.Permissions).ToListAsync(ct);
        var counts = (await _db.UserRoles.GroupBy(ur => ur.RoleId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.N);
        var order = RoleNames.All.ToList();
        // The default roles first, in the order the product defines them; agency roles after, by name.
        return roles
            .OrderBy(r => order.IndexOf(r.Name) >= 0 ? order.IndexOf(r.Name) : int.MaxValue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => SettingsRules.ToDto(r, counts.GetValueOrDefault(r.Id)))
            .ToList();
    }
}

// ── Roles: create, rename, delete ───────────────────────────────────────

/// <summary>A new role starts with no access to any page; the Permissions grid grants it.</summary>
public sealed record CreateRoleCommand(string Name, string? Description) : IRequest<RoleDto>;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Role name is required.")
            .MaximumLength(SettingsRules.MaxRoleName)
            .Must(n => n is null || SettingsRules.RoleNamePattern.IsMatch(n.Trim())).WithMessage("Use letters, numbers, spaces, - & and / only.");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, RoleDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentTenant _tenant;
    public CreateRoleCommandHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentTenant tenant) { _db = db; _permissions = permissions; _tenant = tenant; }

    public async Task<RoleDto> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Create, ct);
        var name = request.Name.Trim();
        if (await _db.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException("duplicate_role", $"A role named '{name}' already exists.");

        var role = new Role { TenantId = _tenant.Value, Name = name, Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(), IsSystem = false };
        SettingsRules.AddEmptyPermissions(role);
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);
        return SettingsRules.ToDto(role, 0);
    }
}

public sealed record UpdateRoleCommand(Guid Id, string Name, string? Description) : IRequest<RoleDto>;

public sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Role name is required.")
            .MaximumLength(SettingsRules.MaxRoleName)
            .Must(n => n is null || SettingsRules.RoleNamePattern.IsMatch(n.Trim())).WithMessage("Use letters, numbers, spaces, - & and / only.");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class UpdateRoleCommandHandler : IRequestHandler<UpdateRoleCommand, RoleDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    public UpdateRoleCommandHandler(IRowDbContext db, IPagePermissionService permissions) { _db = db; _permissions = permissions; }

    public async Task<RoleDto> Handle(UpdateRoleCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);
        var role = await SettingsRules.LoadRoleAsync(_db, request.Id, ct);
        var name = request.Name.Trim();
        if (SettingsRules.IsLocked(role))
            throw new ConflictException("admin_locked", "The Admin role cannot be changed.");
        // The product refers to the default roles by name (page rules, step gating), so only their description may change.
        if (role.IsSystem && name != role.Name)
            throw new ConflictException("system_role", $"'{role.Name}' is a built-in role; only its description can be changed.");
        if (name != role.Name && await _db.Roles.AnyAsync(r => r.Id != role.Id && r.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException("duplicate_role", $"A role named '{name}' already exists.");

        role.Name = name;
        role.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        await _db.SaveChangesAsync(ct);
        return await SettingsRules.ToDtoAsync(_db, role, ct);
    }
}

public sealed record DeleteRoleCommand(Guid Id) : IRequest;

public sealed class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    public DeleteRoleCommandHandler(IRowDbContext db, IPagePermissionService permissions) { _db = db; _permissions = permissions; }

    public async Task Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Delete, ct);
        var role = await SettingsRules.LoadRoleAsync(_db, request.Id, ct);
        if (role.IsSystem || SettingsRules.IsLocked(role))
            throw new ConflictException("system_role", $"'{role.Name}' is a built-in role and cannot be deleted.");
        if (await _db.UserRoles.AnyAsync(ur => ur.RoleId == role.Id, ct))
            throw new ConflictException("role_in_use", $"'{role.Name}' still has users. Move them to another role first.");

        _db.RolePagePermissions.RemoveRange(role.Permissions);
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct);
    }
}

// ── Roles: the permission grid ──────────────────────────────────────────

public sealed record SetRolePermissionsCommand(Guid RoleId, IReadOnlyList<RolePermissionDto> Permissions) : IRequest<RoleDto>;

public sealed class SetRolePermissionsCommandValidator : AbstractValidator<SetRolePermissionsCommand>
{
    public SetRolePermissionsCommandValidator()
    {
        RuleFor(x => x.Permissions).NotNull().WithMessage("Permissions are required.");
        RuleFor(x => x.Permissions).Must(p => p is null || p.Count <= PageKeys.All.Count).WithMessage("Too many pages.");
        RuleForEach(x => x.Permissions).ChildRules(p =>
        {
            p.RuleFor(x => x.PageKey).Must(PageKeys.IsKnown).WithMessage("Unknown page.");
            p.RuleFor(x => x).Must(x => !(x.CanCreate || x.CanUpdate || x.CanDelete) || x.CanRead)
                .WithMessage("Create, update and delete need read access too.");
        });
        RuleFor(x => x.Permissions).Must(p => p is null || p.Select(x => x.PageKey).Distinct().Count() == p.Count).WithMessage("A page is listed twice.");
    }
}

public sealed class SetRolePermissionsCommandHandler : IRequestHandler<SetRolePermissionsCommand, RoleDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentTenant _tenant;
    public SetRolePermissionsCommandHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentTenant tenant) { _db = db; _permissions = permissions; _tenant = tenant; }

    public async Task<RoleDto> Handle(SetRolePermissionsCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);
        var role = await SettingsRules.LoadRoleAsync(_db, request.RoleId, ct);
        if (SettingsRules.IsLocked(role))
            throw new ConflictException("admin_locked", "The Admin role always has full access and cannot be changed.");

        foreach (var p in request.Permissions)
        {
            var flags = new PermissionFlags(p.CanCreate, p.CanRead, p.CanUpdate, p.CanDelete);
            var row = role.Permissions.FirstOrDefault(x => x.PageKey == p.PageKey);
            if (row is null)
            {
                row = new RolePagePermission { TenantId = _tenant.Value, RoleId = role.Id, PageKey = p.PageKey };
                role.Permissions.Add(row);
                _db.RolePagePermissions.Add(row);
            }
            row.CanCreate = flags.Create; row.CanRead = flags.Read; row.CanUpdate = flags.Update; row.CanDelete = flags.Delete;
            row.Template = PermissionTemplateCalculator.TemplateFor(flags);
        }
        await _db.SaveChangesAsync(ct);
        return await SettingsRules.ToDtoAsync(_db, role, ct);
    }
}

// ── Users ───────────────────────────────────────────────────────────────

public sealed record ListSettingsUsersQuery : IRequest<IReadOnlyList<SettingsUserDto>>;

public sealed class ListSettingsUsersQueryHandler : IRequestHandler<ListSettingsUsersQuery, IReadOnlyList<SettingsUserDto>>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    public ListSettingsUsersQueryHandler(IRowDbContext db, IPagePermissionService permissions) { _db = db; _permissions = permissions; }

    public async Task<IReadOnlyList<SettingsUserDto>> Handle(ListSettingsUsersQuery request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Read, ct);
        var users = await _db.AppUsers.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).OrderBy(u => u.Username).ToListAsync(ct);
        return users.Select(SettingsRules.ToDto).ToList();
    }
}

public sealed record CreateSettingsUserCommand(string Username, string FirstName, string LastName, string Email, string Password, Guid RoleId) : IRequest<SettingsUserDto>;

public sealed class CreateSettingsUserCommandValidator : AbstractValidator<CreateSettingsUserCommand>
{
    public CreateSettingsUserCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().WithMessage("Username is required.")
            .MinimumLength(3).MaximumLength(64)
            .Must(u => u is null || SettingsRules.UsernamePattern.IsMatch(SettingsRules.NormalizeUsername(u))).WithMessage("Use lower-case letters, numbers, . _ and - only.");
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("First name is required.").MaximumLength(120);
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Last name is required.").MaximumLength(120);
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Password).ValidNewPassword();
        RuleFor(x => x.RoleId).NotEmpty().WithMessage("Role is required.");
    }
}

public sealed class CreateSettingsUserCommandHandler : IRequestHandler<CreateSettingsUserCommand, SettingsUserDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly IPasswordHasher _hasher;
    private readonly ICurrentTenant _tenant;
    public CreateSettingsUserCommandHandler(IRowDbContext db, IPagePermissionService permissions, IPasswordHasher hasher, ICurrentTenant tenant)
    { _db = db; _permissions = permissions; _hasher = hasher; _tenant = tenant; }

    public async Task<SettingsUserDto> Handle(CreateSettingsUserCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Create, ct);
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, ct) ?? throw new NotFoundException("Role", request.RoleId);
        var username = SettingsRules.NormalizeUsername(request.Username);
        var email = AuthValidation.NormalizeEmail(request.Email);

        // Sign-in names and emails are unique across the whole platform (login needs no agency hint), so check every agency.
        if (await _db.AppUsers.IgnoreQueryFilters().AnyAsync(u => u.Username == username || u.Email == username, ct))
            throw new ConflictException("duplicate_username", $"The username '{username}' is taken.");
        if (await _db.AppUsers.IgnoreQueryFilters().AnyAsync(u => u.Email == email || u.Username == email, ct))
            throw new ConflictException("duplicate_email", "That email is already used by another account.");

        var user = new AppUser
        {
            TenantId = _tenant.Value, Username = username, Email = email, IsActive = true,
            FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(),
            PasswordHash = _hasher.Hash(request.Password), PasswordSetAt = DateTime.UtcNow,
        };
        user.UserRoles.Add(new UserRole { TenantId = _tenant.Value, UserId = user.Id, RoleId = role.Id, Role = role });
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync(ct);
        return SettingsRules.ToDto(user);
    }
}

public sealed record UpdateSettingsUserCommand(Guid Id, string FirstName, string LastName, string Email, Guid RoleId, bool IsActive) : IRequest<SettingsUserDto>;

public sealed class UpdateSettingsUserCommandValidator : AbstractValidator<UpdateSettingsUserCommand>
{
    public UpdateSettingsUserCommandValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("First name is required.").MaximumLength(120);
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Last name is required.").MaximumLength(120);
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.RoleId).NotEmpty().WithMessage("Role is required.");
    }
}

public sealed class UpdateSettingsUserCommandHandler : IRequestHandler<UpdateSettingsUserCommand, SettingsUserDto>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly ICurrentUser _me;
    private readonly ICurrentTenant _tenant;
    public UpdateSettingsUserCommandHandler(IRowDbContext db, IPagePermissionService permissions, ICurrentUser me, ICurrentTenant tenant)
    { _db = db; _permissions = permissions; _me = me; _tenant = tenant; }

    public async Task<SettingsUserDto> Handle(UpdateSettingsUserCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);
        var user = await _db.AppUsers.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new NotFoundException("User", request.Id);
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId, ct) ?? throw new NotFoundException("Role", request.RoleId);
        var email = AuthValidation.NormalizeEmail(request.Email);

        if (user.Id == _me.Id && !request.IsActive)
            throw new ConflictException("cannot_deactivate_self", "You cannot deactivate your own account.");
        var isAdminNow = user.UserRoles.Any(ur => ur.Role?.Name == RoleNames.Admin);
        var staysActiveAdmin = request.IsActive && role.Name == RoleNames.Admin;
        if (isAdminNow && !staysActiveAdmin)
            await SettingsRules.EnsureAnotherAdminAsync(_db, user.Id, ct);

        if (email != user.Email && await _db.AppUsers.IgnoreQueryFilters().AnyAsync(u => u.Id != user.Id && (u.Email == email || u.Username == email), ct))
            throw new ConflictException("duplicate_email", "That email is already used by another account.");

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = email;
        user.IsActive = request.IsActive;

        // One role per person (U): replace whatever they had. A role change reaches their token on their next sign-in.
        if (user.UserRoles.Count != 1 || user.UserRoles.First().RoleId != role.Id)
        {
            foreach (var old in user.UserRoles.ToList()) _db.UserRoles.Remove(old);
            user.UserRoles.Clear();
            var link = new UserRole { TenantId = _tenant.Value, UserId = user.Id, RoleId = role.Id, Role = role };
            user.UserRoles.Add(link);
            _db.UserRoles.Add(link);
        }
        await _db.SaveChangesAsync(ct);
        return SettingsRules.ToDto(user);
    }
}

public sealed record ResetSettingsUserPasswordCommand(Guid Id, string Password) : IRequest;

public sealed class ResetSettingsUserPasswordCommandValidator : AbstractValidator<ResetSettingsUserPasswordCommand>
{
    public ResetSettingsUserPasswordCommandValidator() => RuleFor(x => x.Password).ValidNewPassword();
}

public sealed class ResetSettingsUserPasswordCommandHandler : IRequestHandler<ResetSettingsUserPasswordCommand>
{
    private readonly IRowDbContext _db;
    private readonly IPagePermissionService _permissions;
    private readonly IPasswordHasher _hasher;
    public ResetSettingsUserPasswordCommandHandler(IRowDbContext db, IPagePermissionService permissions, IPasswordHasher hasher)
    { _db = db; _permissions = permissions; _hasher = hasher; }

    public async Task Handle(ResetSettingsUserPasswordCommand request, CancellationToken ct)
    {
        await _permissions.EnsureAsync(PageKeys.Administration, PermissionAction.Update, ct);
        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == request.Id, ct) ?? throw new NotFoundException("User", request.Id);
        user.PasswordHash = _hasher.Hash(request.Password);
        user.PasswordSetAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
