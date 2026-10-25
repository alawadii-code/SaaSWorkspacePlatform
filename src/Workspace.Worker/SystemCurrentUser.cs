using Workspace.Application.Abstractions;
using Workspace.Domain.Common;

namespace Workspace.Worker;

/// <summary>
/// The worker's actor identity. There is no HTTP request and no signed-in
/// user, so audit entries written from background work are attributed to the
/// system rather than borrowing someone's identity.
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public const string SystemActorEmail = "system@worker.local";

    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public string? Email => SystemActorEmail;

    public string? IpAddress => null;

    public string? UserAgent => "workspace-worker";

    public string? CorrelationId => null;

    /// <summary>
    /// Always throws. Background jobs must not run code paths that assume a
    /// signed-in user; if one does, this fails loudly instead of silently
    /// acting as someone.
    /// </summary>
    public Guid RequireUserId() =>
        throw new UnauthorizedException("Background work has no authenticated user.");
}
