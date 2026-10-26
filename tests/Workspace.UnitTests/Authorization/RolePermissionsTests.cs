using Workspace.Domain.Authorization;
using Xunit;

namespace Workspace.UnitTests.Authorization;

/// <summary>The permission matrix is the authorization contract, so itz tested directly insteada only thru the API: a change here that widens access shud fail a test, not surface in a code review.</summary>
public class RolePermissionsTests
{
    [Theory]
    [InlineData(OrganizationRole.Viewer, Permission.OrganizationRead)]
    [InlineData(OrganizationRole.Viewer, Permission.MemberRead)]
    [InlineData(OrganizationRole.Viewer, Permission.ProjectRead)]
    [InlineData(OrganizationRole.Viewer, Permission.TaskRead)]
    [InlineData(OrganizationRole.Member, Permission.TaskCreate)]
    [InlineData(OrganizationRole.Member, Permission.TaskUpdateOwn)]
    [InlineData(OrganizationRole.Member, Permission.TaskAssign)]
    [InlineData(OrganizationRole.Admin, Permission.MemberInvite)]
    [InlineData(OrganizationRole.Admin, Permission.MemberRoleUpdate)]
    [InlineData(OrganizationRole.Admin, Permission.MemberRemove)]
    [InlineData(OrganizationRole.Admin, Permission.ProjectCreate)]
    [InlineData(OrganizationRole.Admin, Permission.ProjectDelete)]
    [InlineData(OrganizationRole.Admin, Permission.TaskUpdateAny)]
    [InlineData(OrganizationRole.Admin, Permission.TaskDelete)]
    [InlineData(OrganizationRole.Admin, Permission.AuditRead)]
    [InlineData(OrganizationRole.Owner, Permission.OrganizationDelete)]
    public void Role_grants_expected_permission(OrganizationRole role, Permission permission) =>
        Assert.True(RolePermissions.Allows(role, permission));

    [Theory]
    [InlineData(OrganizationRole.Viewer, Permission.TaskCreate)]
    [InlineData(OrganizationRole.Viewer, Permission.TaskUpdateOwn)]
    [InlineData(OrganizationRole.Viewer, Permission.TaskUpdateAny)]
    [InlineData(OrganizationRole.Viewer, Permission.ProjectCreate)]
    [InlineData(OrganizationRole.Viewer, Permission.MemberInvite)]
    [InlineData(OrganizationRole.Viewer, Permission.AuditRead)]
    [InlineData(OrganizationRole.Member, Permission.TaskUpdateAny)]
    [InlineData(OrganizationRole.Member, Permission.TaskDelete)]
    [InlineData(OrganizationRole.Member, Permission.ProjectCreate)]
    [InlineData(OrganizationRole.Member, Permission.ProjectUpdate)]
    [InlineData(OrganizationRole.Member, Permission.ProjectDelete)]
    [InlineData(OrganizationRole.Member, Permission.MemberInvite)]
    [InlineData(OrganizationRole.Member, Permission.MemberRoleUpdate)]
    [InlineData(OrganizationRole.Member, Permission.MemberRemove)]
    [InlineData(OrganizationRole.Member, Permission.AuditRead)]
    [InlineData(OrganizationRole.Member, Permission.InvitationRead)]
    [InlineData(OrganizationRole.Admin, Permission.OrganizationDelete)]
    public void Role_does_not_grant_permission(OrganizationRole role, Permission permission) =>
        Assert.False(RolePermissions.Allows(role, permission));

    /// <summary>Each role is a superset of the one below it.</summary>
    [Fact]
    public void Roles_are_cumulative()
    {
        var viewer = RolePermissions.For(OrganizationRole.Viewer);
        var member = RolePermissions.For(OrganizationRole.Member);
        var admin = RolePermissions.For(OrganizationRole.Admin);
        var owner = RolePermissions.For(OrganizationRole.Owner);

        Assert.True(viewer.IsSubsetOf(member));
        Assert.True(member.IsSubsetOf(admin));
        Assert.True(admin.IsSubsetOf(owner));
    }

    [Fact]
    public void Viewer_has_only_read_permissions()
    {
        var readOnly = new[]
        {
            Permission.OrganizationRead,
            Permission.MemberRead,
            Permission.ProjectRead,
            Permission.TaskRead,
        };

        Assert.Equal(readOnly.Length, RolePermissions.For(OrganizationRole.Viewer).Count);
        Assert.All(readOnly, permission => Assert.True(RolePermissions.Allows(OrganizationRole.Viewer, permission)));
    }

    [Fact]
    public void Owner_holds_every_defined_permission()
    {
        var all = Enum.GetValues<Permission>();
        var owner = RolePermissions.For(OrganizationRole.Owner);

        Assert.Equal(all.Length, owner.Count);
    }

    /// <summary>An unmapped role value — a cast integer, or an enum member added w/o a matrix entry — must grant nuthin insteada everything.</summary>
    [Fact]
    public void Unknown_role_fails_closed()
    {
        var unknown = (OrganizationRole)999;

        Assert.Empty(RolePermissions.For(unknown));
        Assert.False(RolePermissions.Allows(unknown, Permission.OrganizationRead));
    }

    [Fact]
    public void Every_role_has_an_explicit_matrix_entry()
    {
        foreach (var role in Enum.GetValues<OrganizationRole>())
        {
            Assert.NotEmpty(RolePermissions.For(role));
        }
    }

    [Fact]
    public void Permission_names_are_sorted_for_stable_client_payloads()
    {
        var names = RolePermissions.NamesFor(OrganizationRole.Admin);

        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal), names);
    }
}
