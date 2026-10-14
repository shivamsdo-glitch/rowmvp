using System.Collections.Concurrent;
using System.Reflection;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Aurigo.RowManagement.Application.Common;

/// <summary>
/// Runs every FluentValidation validator for a request before its handler. This is the only place
/// input is validated; the domain enforces invariants, not input shape.
/// </summary>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    // Postgres text and jsonb cannot store U+0000; left alone it surfaces as a 500 deep inside the save.
    // Rejecting it here, for every string on every request, turns that into a normal 400.
    private static readonly PropertyInfo[] StringProperties = typeof(TRequest)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType == typeof(string) && p.CanRead && p.GetIndexParameters().Length == 0)
        .ToArray();

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var nul = StringProperties
            .Where(p => p.GetValue(request) is string value && value.Contains('\0'))
            .Select(p => new ValidationFailure(p.Name, "Must not contain null characters."))
            .ToList();
        if (nul.Count > 0) throw new ValidationException(nul);

        if (_validators.Any())
        {
            var ctx = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(ctx, ct)));
            var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
            if (failures.Count > 0)
                throw new ValidationException(failures);
        }
        return await next();
    }
}
