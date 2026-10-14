namespace Aurigo.RowManagement.Application.Auth;

public sealed record UserDto(
    Guid Id,
    string FirstName,
    string LastName,
    string FullName,
    string Initials,
    string Email,
    IReadOnlyList<string> Roles,
    Guid TenantId,
    string? TenantName);

public sealed record AuthResponse(string Token, string TokenType, DateTime ExpiresAtUtc, UserDto User);

public sealed record DevTokenResponse(
    string Token,
    string TokenType,
    DateTime ExpiresAtUtc,
    IReadOnlyList<string> Roles,
    Guid TenantId,
    Guid UserId);

public sealed record MessageResponse(string Message);
