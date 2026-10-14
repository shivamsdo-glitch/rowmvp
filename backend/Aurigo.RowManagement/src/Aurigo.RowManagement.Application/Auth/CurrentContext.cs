namespace Aurigo.RowManagement.Application.Auth;

/// <summary>The tenant of the current request, resolved from the JWT <c>tenant_id</c> claim.</summary>
public interface ICurrentTenant
{
    Guid Value { get; }
    bool IsAvailable { get; }
}

/// <summary>The authenticated caller, resolved from JWT claims.</summary>
public interface ICurrentUser
{
    Guid Id { get; }
    string Email { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsAuthenticated { get; }
}
