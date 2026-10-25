namespace Workspace.Domain.Audit;

/// <summary>
/// Canonical audit action names. Constants rather than free-form strings so the
/// trail stays queryable and typos cannot create a parallel vocabulary.
/// </summary>
public static class AuditActions
{
    public const string UserRegistered = "user.registered";
    public const string UserLoggedIn = "user.logged_in";
    public const string UserLoginFailed = "user.login_failed";
    public const string UserLoggedOut = "user.logged_out";
    public const string TokenRefreshReuseDetected = "token.refresh_reuse_detected";

    public const string OrganizationCreated = "organization.created";
    public const string OrganizationUpdated = "organization.updated";
    public const string OrganizationDeleted = "organization.deleted";

    public const string MemberInvited = "member.invited";
    public const string MemberInvitationRevoked = "member.invitation_revoked";
    public const string MemberInvitationAccepted = "member.invitation_accepted";
    public const string MemberRoleChanged = "member.role_changed";
    public const string MemberRemoved = "member.removed";
    public const string MemberLeft = "member.left";

    public const string ProjectCreated = "project.created";
    public const string ProjectUpdated = "project.updated";
    public const string ProjectDeleted = "project.deleted";

    public const string TaskCreated = "task.created";
    public const string TaskUpdated = "task.updated";
    public const string TaskAssigned = "task.assigned";
    public const string TaskDeleted = "task.deleted";
}

/// <summary>Canonical resource type names used in audit entries.</summary>
public static class AuditResources
{
    public const string User = "user";
    public const string Organization = "organization";
    public const string Membership = "membership";
    public const string Invitation = "invitation";
    public const string Project = "project";
    public const string Task = "task";
    public const string Session = "session";
}
