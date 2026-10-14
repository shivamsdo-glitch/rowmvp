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

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        // Postgres text and jsonb cannot store U+0000; left alone it surfaces as a 500 deep inside the save.
        // Rejecting it here, anywhere in the request (nested objects, lists, dictionaries), turns that into a normal 400.
        var nul = NullCharacterScanner.Find(request)
            .Select(path => new ValidationFailure(path, "Must not contain null characters."))
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
