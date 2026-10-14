using System.Text.Json;
using Aurigo.RowManagement.Domain.Errors;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aurigo.RowManagement.Api.Middleware;

/// <summary>
/// Maps every exception that escapes a request to an RFC 7807 ProblemDetails response. Expected
/// domain failures become 4xx with their own code; anything unexpected becomes a generic 500 whose
/// details are only in the server log (never leaked to the client).
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _log;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task Invoke(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected mid-request: expected, nobody is listening for a response.
            _log.LogDebug("Request cancelled by client: {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        }
        catch (ValidationException ex)
        {
            var errors = ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
            await WriteValidation(ctx, errors);
        }
        catch (NotFoundException ex) { await Write(ctx, StatusCodes.Status404NotFound, ex.Code, ex.Message); }
        catch (ForbiddenException ex) { await Write(ctx, StatusCodes.Status403Forbidden, ex.Code, ex.Message); }
        catch (AuthenticationFailedException ex) { await Write(ctx, StatusCodes.Status401Unauthorized, ex.Code, ex.Message); }
        catch (ConflictException ex) { await Write(ctx, StatusCodes.Status409Conflict, ex.Code, ex.Message); }
        catch (DomainException ex) { await Write(ctx, StatusCodes.Status400BadRequest, ex.Code, ex.Message); }
        catch (DbUpdateConcurrencyException ex)
        {
            _log.LogWarning(ex, "Concurrency conflict");
            await Write(ctx, StatusCodes.Status409Conflict, "concurrent_edit",
                "The record was changed by someone else. Refresh and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two requests raced past an application-level duplicate check and the unique index caught the loser.
            _log.LogInformation(ex, "Unique constraint hit for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            await Write(ctx, StatusCodes.Status409Conflict, "already_exists", "That record already exists.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled exception for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            await Write(ctx, StatusCodes.Status500InternalServerError, "internal_error", "An unexpected error occurred.");
        }
    }

    private static Task Write(HttpContext ctx, int status, string code, string detail) =>
        WriteProblem(ctx, new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = detail,
            Type = $"urn:aurigo:row:problem:{code}",
        });

    private static Task WriteValidation(HttpContext ctx, Dictionary<string, string[]> errors) =>
        WriteProblem(ctx, new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "validation_failed",
            Detail = "One or more validation errors occurred.",
            Type = "urn:aurigo:row:problem:validation_failed",
        });

    private static async Task WriteProblem(HttpContext ctx, ProblemDetails problem)
    {
        if (ctx.Response.HasStarted) return; // too late to change the response

        problem.Extensions["traceId"] = ctx.TraceIdentifier;
        ctx.Response.Clear();
        ctx.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(problem, problem.GetType(), Json));
    }
}
