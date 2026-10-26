using System.Collections.Frozen;
using Workspace.Application.Abstractions;
using Workspace.Domain.Authorization;
using Workspace.Domain.Common;

namespace Workspace.Infrastructure.Tenancy;

/// <summary>Per-request tenant state.</summary>
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
