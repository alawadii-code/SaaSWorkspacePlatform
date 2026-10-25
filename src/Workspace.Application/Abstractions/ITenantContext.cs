using Workspace.Domain.Authorization;
using Workspace.Domain.Common;

namespace Workspace.Application.Abstractions;

/// <summary>
/// The caller's resolved position inside one organization for the current
/// request: which tenant, which user, which role, and therefore which
/// permissions.
/// </summary>
/// <remarks>
/// Populated server-side by the tenant resolution middleware from the
/// authenticated principal plus a membership lookup. Nothing a client sends —
/// body, header or query string — can set the role or the organization.
/// </remarks>
public interface ITenantContext
{
    /// <summary>True once a membership has been resolved for this request.</summary>
    bool IsResolved { get; }

    Guid OrganizationId { get; }

    Guid UserId { get; }

    OrganizationRole Role { get; }

    IReadOnlySet<Permission> Permissions { get; }

    /// <summary>Whether the caller holds <paramref name="permission"/>.</summary>
    bool Has(Permission permission);

    /// <summary>
    /// Throws <see cref="ForbiddenException"/> unless the caller holds
    /// <paramref name="permission"/>. Used by services so that an endpoint
    /// which forgets its authorization filter is still not an open door.
    /// </summary>
    void Require(Permission permission);
}

/// <summary>
/// Write side of <see cref="ITenantContext"/>, available only to the hosting
/// layer's middleware so request handlers cannot reassign the tenant.
/// </summary>
public interface ITenantContextSetter
{
    void Set(Guid organizationId, Guid userId, OrganizationRole role);

    void Clear();
}
