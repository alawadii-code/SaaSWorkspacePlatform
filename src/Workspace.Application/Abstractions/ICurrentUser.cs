namespace Workspace.Application.Abstractions;

/// <summary>
/// The authenticated principal and request metadata needed for audit entries.
/// Implemented over <c>HttpContext</c> in the API and as a fixed system actor
/// in the background worker.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Authenticated user id, or <c>null</c> for anonymous and system callers.</summary>
    Guid? UserId { get; }

    string? Email { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    /// <summary>Per-request identifier echoed in responses and stored on audit rows.</summary>
    string? CorrelationId { get; }

    /// <summary>Authenticated user id, or throws when the caller is anonymous.</summary>
    Guid RequireUserId();
}
