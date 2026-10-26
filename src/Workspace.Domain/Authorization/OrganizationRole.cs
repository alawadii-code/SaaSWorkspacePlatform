namespace Workspace.Domain.Authorization;

/// <summary>A user's role within one organization.</summary>
public enum OrganizationRole
{
    Viewer = 10,
    Member = 20,
    Admin = 30,
    Owner = 40,
}
