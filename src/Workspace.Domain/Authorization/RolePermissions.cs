using System.Collections.Frozen;

namespace Workspace.Domain.Authorization;

/// <summary>
/// The single source of truth for what each role may do.
/// </summary>
/// <remarks>
/// <para>
/// This is a pure, dependency-free lookup: it is unit-tested directly, and the
/// API's authorization filter is the only production caller. The frontend
/// receives the caller's permission list purely to decide what to render —
/// every request is authorized again on the server.
/// </para>
/// <para>
/// Rules that depend on <em>who</em> the target is (an admin may not demote an
/// owner; the last owner may not be removed) are not expressible in a static
/// matrix and live in <see cref="MembershipRules"/>.
/// </para>
/// </remarks>
public static class RolePermissions
{
    private static readonly Permission[] ViewerPermissions =
    [
        Permission.OrganizationRead,
        Permission.MemberRead,
        Permission.ProjectRead,
        Permission.TaskRead,
    ];

    private static readonly Permission[] MemberPermissions =
    [
        .. ViewerPermissions,
        Permission.TaskCreate,
        Permission.TaskUpdateOwn,
        Permission.TaskAssign,
    ];

    private static readonly Permission[] AdminPermissions =
    [
        .. MemberPermissions,
        Permission.OrganizationUpdate,
        Permission.MemberInvite,
        Permission.MemberRoleUpdate,
        Permission.MemberRemove,
        Permission.InvitationRead,
        Permission.InvitationRevoke,
        Permission.ProjectCreate,
        Permission.ProjectUpdate,
        Permission.ProjectDelete,
        Permission.TaskUpdateAny,
        Permission.TaskDelete,
        Permission.AuditRead,
    ];

    private static readonly Permission[] OwnerPermissions =
    [
        .. AdminPermissions,
        Permission.OrganizationDelete,
    ];

    private static readonly FrozenDictionary<OrganizationRole, FrozenSet<Permission>> Matrix =
        new Dictionary<OrganizationRole, FrozenSet<Permission>>
        {
            [OrganizationRole.Viewer] = ViewerPermissions.ToFrozenSet(),
            [OrganizationRole.Member] = MemberPermissions.ToFrozenSet(),
            [OrganizationRole.Admin] = AdminPermissions.ToFrozenSet(),
            [OrganizationRole.Owner] = OwnerPermissions.ToFrozenSet(),
        }.ToFrozenDictionary();

    /// <summary>All permissions granted to <paramref name="role"/>.</summary>
    public static IReadOnlySet<Permission> For(OrganizationRole role) =>
        Matrix.TryGetValue(role, out var permissions)
            ? permissions
            // Fail closed: an unmapped role gets nothing rather than everything.
            : FrozenSet<Permission>.Empty;

    /// <summary>Whether <paramref name="role"/> grants <paramref name="permission"/>.</summary>
    public static bool Allows(OrganizationRole role, Permission permission) =>
        For(role).Contains(permission);

    /// <summary>
    /// Permission names for the API's <c>/me</c> payload, so the frontend can
    /// hide controls the caller cannot use. Presentation only — not a security
    /// boundary.
    /// </summary>
    public static string[] NamesFor(OrganizationRole role) =>
        [.. For(role).Select(p => p.ToString()).Order(StringComparer.Ordinal)];
}
