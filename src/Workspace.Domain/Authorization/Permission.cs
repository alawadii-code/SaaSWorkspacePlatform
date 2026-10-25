namespace Workspace.Domain.Authorization;

/// <summary>
/// Every action the API can authorize. Endpoints declare the permission they
/// need; they never check roles directly, so adding a role is a change to the
/// matrix in <see cref="RolePermissions"/> and nothing else.
/// </summary>
public enum Permission
{
    OrganizationRead = 1,
    OrganizationUpdate,
    OrganizationDelete,

    MemberRead,
    MemberInvite,
    MemberRoleUpdate,
    MemberRemove,

    InvitationRead,
    InvitationRevoke,

    ProjectRead,
    ProjectCreate,
    ProjectUpdate,
    ProjectDelete,

    TaskRead,
    TaskCreate,

    /// <summary>Update any task in the organization.</summary>
    TaskUpdateAny,

    /// <summary>Update only tasks the caller created or is assigned to.</summary>
    TaskUpdateOwn,

    TaskAssign,
    TaskDelete,

    AuditRead,
}
