namespace Workspace.Domain.Authorization;

/// <summary>
/// A user's role within one organization. Stored per membership, never on the
/// user, because the same person can hold different roles in different tenants.
/// </summary>
/// <remarks>
/// Numeric values are ordered by privilege and are persisted, so they must not
/// be renumbered. Ordering is used only for "can this actor act on that
/// actor" comparisons, never to derive permissions — those come from the
/// explicit matrix in <see cref="RolePermissions"/>.
/// </remarks>
public enum OrganizationRole
{
    Viewer = 10,
    Member = 20,
    Admin = 30,
    Owner = 40,
}
