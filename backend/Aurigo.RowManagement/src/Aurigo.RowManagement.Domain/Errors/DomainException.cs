namespace Aurigo.RowManagement.Domain.Errors;

/// <summary>Base for expected, client-visible failures. The API middleware maps each subtype to a ProblemDetails status.</summary>
public abstract class DomainException : Exception
{
    public string Code { get; }

    protected DomainException(string code, string message) : base(message)
    {
        Code = code;
    }
}

public sealed class NotFoundException : DomainException
{
    public NotFoundException(string entity, object key)
        : base("not_found", $"{entity} with key '{key}' was not found.") { }
}

public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string message) : base("forbidden", message) { }
}

/// <summary>Credentials, token or account state prevent authentication. Mapped to HTTP 401.</summary>
public sealed class AuthenticationFailedException : DomainException
{
    public AuthenticationFailedException(string code, string message) : base(code, message) { }
}

/// <summary>The request is valid but conflicts with current state. Mapped to HTTP 409.</summary>
public sealed class ConflictException : DomainException
{
    public ConflictException(string code, string message) : base(code, message) { }
}

/// <summary>The request cannot be processed as sent (e.g. an invalid or expired link). Mapped to HTTP 400.</summary>
public sealed class InvalidRequestException : DomainException
{
    public InvalidRequestException(string code, string message) : base(code, message) { }
}

/// <summary>A domain invariant or state-transition rule was violated. Mapped to HTTP 400.</summary>
public sealed class BusinessRuleException : DomainException
{
    public BusinessRuleException(string message) : base("business_rule_violation", message) { }
}
