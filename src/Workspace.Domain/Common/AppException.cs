namespace Workspace.Domain.Common;

/// <summary>Base class for failures that are part of the API contract.</summary>
public abstract class AppException : Exception
{
    protected AppException(string code, string message) : base(message) => Code = code;

    /// <summary>Stable, machine-readable error code (snake_case).</summary>
    public string Code { get; }

    public abstract int StatusCode { get; }
}

/// <summary>Caller is not authenticated, or presented an invalid/expired credential.</summary>
public sealed class UnauthorizedException(string message = "Authentication is required.")
    : AppException("unauthorized", message)
{
    public override int StatusCode => StatusCodes.Unauthorized;
}

/// <summary>Caller is authenticated but lacks the permission required for the operation.</summary>
public sealed class ForbiddenException(string message = "You do not have permission to perform this action.")
    : AppException("forbidden", message)
{
    public override int StatusCode => StatusCodes.Forbidden;
}

/// <summary>Resource does not exist — or exists in another tenant, which is reported identically on purpose so the API cant be used to probe for the existence of other organizations' data.</summary>
public sealed class NotFoundException(string resource = "Resource")
    : AppException("not_found", $"{resource} was not found.")
{
    public override int StatusCode => StatusCodes.NotFound;
}

/// <summary>Request conflicts w/ current state (duplicate, or a violated invariant).</summary>
public sealed class ConflictException(string message) : AppException("conflict", message)
{
    public override int StatusCode => StatusCodes.Conflict;
}

/// <summary>Request body or query failed validation.</summary>
public sealed class ValidationFailedException : AppException
{
    public ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
        : base("validation_failed", "One or more fields are invalid.") => Errors = errors;

    public ValidationFailedException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public override int StatusCode => StatusCodes.UnprocessableEntity;
}

/// <summary>Status codes used by the domain.</summary>
internal static class StatusCodes
{
    internal const int Unauthorized = 401;
    internal const int Forbidden = 403;
    internal const int NotFound = 404;
    internal const int Conflict = 409;
    internal const int UnprocessableEntity = 422;
}
