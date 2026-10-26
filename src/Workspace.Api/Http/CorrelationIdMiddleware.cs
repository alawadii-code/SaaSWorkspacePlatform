namespace Workspace.Api.Http;

/// <summary>Gives evry request an id, echoes it back, and puts it in the log scope so one identifier ties together a client report, the server log and the audit rows the request produced.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Sanitize(context.Request.Headers[HttpCurrentUser.CorrelationHeader])
            ?? Guid.CreateVersion7().ToString("n");

        context.Items[HttpCurrentUser.CorrelationHeader] = correlationId;
        context.Response.Headers[HttpCurrentUser.CorrelationHeader] = correlationId;

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
        });

        await next(context);
    }

    /// <summary>Client-supplied ids are accepted so a frontend can trace a call end to end, but only after being bounded and stripped of anythin that cud forge a log line or a response header.</summary>
    private static string? Sanitize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var trimmed = candidate.Trim();

        if (trimmed.Length > MaxLength)
        {
            trimmed = trimmed[..MaxLength];
        }

        return trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? trimmed
            : null;
    }
}
