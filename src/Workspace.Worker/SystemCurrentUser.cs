using Workspace.Application.Abstractions;
using Workspace.Domain.Common;

namespace Workspace.Worker;

/// <summary>The worker's actor identity.</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public const string SystemActorEmail = "system@worker.local";

    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public string? Email => SystemActorEmail;

    public string? IpAddress => null;

    public string? UserAgent => "workspace-worker";

    public string? CorrelationId => null;

    /// <summary>Always throws.</summary>
    public Guid RequireUserId() =>
        throw new UnauthorizedException("Background work has no authenticated user.");
}
