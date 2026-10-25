using System.Collections.Frozen;
using Workspace.Application.Abstractions;
using Workspace.Domain.Authorization;
using Workspace.Domain.Common;

namespace Workspace.Infrastructure.Tenancy;

/// <summary>
/// Per-request tenant state. Registered scoped and populated once, by the
/// API's tenant resolution middleware, from the authenticated user id plus a
/// membership lookup.
/// </summary>
/// <remarks>
/// The read interface is injected everywhere; the write interface is resolved
/// only by the middleware. Request handlers therefore cannot change which
/// organization they are operating in, and a second <see cref="Set"/> on an
/// already-resolved context is a bug rather than a feature.
/// </remarks>
public sealed class TenantContext : ITenantContext, ITenantContextSetter
{
    public bool IsResolved { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid UserId { get; private set; }

    public OrganizationRole Role { get; private set; }

    public IReadOnlySet<Permission> Permissions { get; private set; } = FrozenSet<Permission>.Empty;

    public bool Has(Permission permission) => IsResolved && Permissions.Contains(permission);

    public void Require(Permission permission)
    {
        if (!IsResolved)
        {
            throw new ForbiddenException("No organization context was resolved for this request.");
        }

        if (!Permissions.Contains(permission))
        {
            throw new ForbiddenException(
                $"Your role ({Role}) does not allow this action.");
        }
    }

    public void Set(Guid organizationId, Guid userId, OrganizationRole role)
    {
        if (IsResolved)
        {
            throw new InvalidOperationException("The tenant context has already been resolved for this request.");
        }

        OrganizationId = organizationId;
        UserId = userId;
        Role = role;
        Permissions = RolePermissions.For(role);
        IsResolved = true;
    }

    public void Clear()
    {
        IsResolved = false;
        OrganizationId = Guid.Empty;
        UserId = Guid.Empty;
        Role = default;
        Permissions = FrozenSet<Permission>.Empty;
    }
}
