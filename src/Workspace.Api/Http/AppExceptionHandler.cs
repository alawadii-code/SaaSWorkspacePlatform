using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Workspace.Domain.Common;

namespace Workspace.Api.Http;

/// <summary>
/// Turns exceptions into RFC 9457 problem responses.
/// </summary>
/// <remarks>
/// <para>
/// Domain failures carry their own status and a stable <c>code</c> clients can
/// branch on. Everything unrecognized becomes a 500 whose body says nothing
/// beyond the correlation id: exception messages can contain connection
/// strings, SQL and file paths, so they are logged, never returned. Only in
/// Development is the detail included, to keep local debugging quick.
/// </para>
/// <para>
/// Unique-constraint violations are mapped here rather than guarded with a
/// pre-check in every service: an existence check followed by an insert is a
/// race, and the database is the only place that can decide atomically.
/// </para>
/// </remarks>
public sealed class AppExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // The client gave up on the request; there is nobody to answer.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var correlationId = httpContext.Items.TryGetValue(HttpCurrentUser.CorrelationHeader, out var value)
            ? value as string
            : null;

        var problem = Map(exception, httpContext);

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception on {Method} {Path} (correlation {CorrelationId}).",
                httpContext.Request.Method,
                httpContext.Request.Path,
                correlationId);
        }
        else
        {
            logger.LogInformation(
                "Request failed with {Status} ({Code}) on {Method} {Path}: {Message}",
                problem.Status,
                problem.Extensions.TryGetValue("code", out var code) ? code : "unknown",
                httpContext.Request.Method,
                httpContext.Request.Path,
                exception.Message);
        }

        problem.Extensions["correlationId"] = correlationId;
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    private ProblemDetails Map(Exception exception, HttpContext httpContext) => exception switch
    {
        ValidationFailedException validation => Problem(
            validation.StatusCode,
            validation.Code,
            "One or more fields are invalid.",
            validation.Message,
            httpContext,
            extensions: new Dictionary<string, object?> { ["errors"] = validation.Errors }),

        AppException app => Problem(
            app.StatusCode,
            app.Code,
            TitleFor(app.StatusCode),
            app.Message,
            httpContext),

        DbUpdateException { InnerException: PostgresException { SqlState: UniqueViolation } } => Problem(
            StatusCodes.Status409Conflict,
            "resource_exists",
            "Conflict",
            "That resource already exists.",
            httpContext),

        DbUpdateException { InnerException: PostgresException { SqlState: ForeignKeyViolation } } => Problem(
            StatusCodes.Status409Conflict,
            "reference_invalid",
            "Conflict",
            "A referenced resource does not exist.",
            httpContext),

        DbUpdateConcurrencyException => Problem(
            StatusCodes.Status409Conflict,
            "concurrent_modification",
            "Conflict",
            "The resource changed while this request was in flight. Reload and try again.",
            httpContext),

        BadHttpRequestException badRequest => Problem(
            StatusCodes.Status400BadRequest,
            "malformed_request",
            "Bad Request",
            environment.IsDevelopment() ? badRequest.Message : "The request could not be parsed.",
            httpContext),

        _ => Problem(
            StatusCodes.Status500InternalServerError,
            "internal_error",
            "Internal Server Error",
            environment.IsDevelopment()
                ? exception.ToString()
                : "An unexpected error occurred. Quote the correlation id when reporting this.",
            httpContext),
    };

    private static ProblemDetails Problem(
        int status,
        string code,
        string title,
        string detail,
        HttpContext httpContext,
        Dictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://docs.workspace.local/errors/{code}",
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
        };

        problem.Extensions["code"] = code;

        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        return problem;
    }

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Content",
        _ => "Request Failed",
    };
}
