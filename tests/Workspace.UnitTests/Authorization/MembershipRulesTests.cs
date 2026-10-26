using Workspace.Domain.Authorization;
using Workspace.Domain.Common;
using Xunit;

namespace Workspace.UnitTests.Authorization;

/// <summary>Membership rules are where privilege escalation and tenant lockout would live if they were going to happen, so each one gets a test for the failure it prevents.</summary>
public class MembershipRulesTests
{
    private static readonly Guid Actor = Guid.CreateVersion7();
    private static readonly Guid Target = Guid.CreateVersion7();

    [Theory]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Owner, true)]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Admin, true)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Admin, true)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Member, true)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Owner, false)]
    [InlineData(OrganizationRole.Member, OrganizationRole.Viewer, false)]
    [InlineData(OrganizationRole.Viewer, OrganizationRole.Viewer, false)]
    public void CanActOnMemberWithRole_respects_hierarchy(
        OrganizationRole actorRole,
        OrganizationRole targetRole,
        bool expected) =>
        Assert.Equal(expected, MembershipRules.CanActOnMemberWithRole(actorRole, targetRole));

    [Theory]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Owner, true)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Admin, true)]
    [InlineData(OrganizationRole.Admin, OrganizationRole.Owner, false)]
    [InlineData(OrganizationRole.Member, OrganizationRole.Member, false)]
    public void CanGrantRole_prevents_escalation_above_actor(
        OrganizationRole actorRole,
        OrganizationRole roleToGrant,
        bool expected) =>
        Assert.Equal(expected, MembershipRules.CanGrantRole(actorRole, roleToGrant));

    [Fact]
    public void Nobody_can_change_their_own_role()
    {
        var error = Assert.Throws<ForbiddenException>(() => MembershipRules.EnsureCanChangeRole(
            actorUserId: Actor,
            targetUserId: Actor,
            actorRole: OrganizationRole.Owner,
            currentRole: OrganizationRole.Owner,
            newRole: OrganizationRole.Admin,
            remainingOwnerCount: 2));

        Assert.Contains("your own role", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Admin_cannot_demote_an_owner()
    {
        Assert.Throws<ForbiddenException>(() => MembershipRules.EnsureCanChangeRole(
            actorUserId: Actor,
            targetUserId: Target,
            actorRole: OrganizationRole.Admin,
            currentRole: OrganizationRole.Owner,
            newRole: OrganizationRole.Member,
            remainingOwnerCount: 2));
    }

    [Fact]
    public void Admin_cannot_promote_anyone_to_owner()
    {
        Assert.Throws<ForbiddenException>(() => MembershipRules.EnsureCanChangeRole(
            actorUserId: Actor,
            targetUserId: Target,
            actorRole: OrganizationRole.Admin,
            currentRole: OrganizationRole.Member,
            newRole: OrganizationRole.Owner,
            remainingOwnerCount: 1));
    }

    [Fact]
    public void Owner_can_promote_a_member_to_admin()
    {
        MembershipRules.EnsureCanChangeRole(
            actorUserId: Actor,
            targetUserId: Target,
            actorRole: OrganizationRole.Owner,
            currentRole: OrganizationRole.Member,
            newRole: OrganizationRole.Admin,
            remainingOwnerCount: 1);
    }

    [Fact]
    public void Setting_the_same_role_is_a_conflict_not_a_silent_success()
    {
        Assert.Throws<ConflictException>(() => MembershipRules.EnsureCanChangeRole(
            actorUserId: Actor,
            targetUserId: Target,
            actorRole: OrganizationRole.Owner,
            currentRole: OrganizationRole.Admin,
            newRole: OrganizationRole.Admin,
            remainingOwnerCount: 1));
    }

    [Fact]
    public void The_last_owner_cannot_be_demoted()
    {
        var error = Assert.Throws<ConflictException>(() => MembershipRules.EnsureCanChangeRole(
            actorUserId: Actor,
            targetUserId: Target,
            actorRole: OrganizationRole.Owner,
            currentRole: OrganizationRole.Owner,
            newRole: OrganizationRole.Admin,
            remainingOwnerCount: 1));

        Assert.Contains("at least one owner", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_second_owner_can_be_demoted()
    {
        MembershipRules.EnsureCanChangeRole(
            actorUserId: Actor,
            targetUserId: Target,
            actorRole: OrganizationRole.Owner,
            currentRole: OrganizationRole.Owner,
            newRole: OrganizationRole.Admin,
            remainingOwnerCount: 2);
    }

    [Fact]
    public void The_last_owner_cannot_be_removed()
    {
        Assert.Throws<ConflictException>(() => MembershipRules.EnsureCanRemoveMember(
            actorUserId: Actor,
            targetUserId: Target,
            actorRole: OrganizationRole.Owner,
            targetRole: OrganizationRole.Owner,
            remainingOwnerCount: 1));
    }

    [Fact]
    public void Removing_yourself_points_at_the_leave_endpoint()
    {
        var error = Assert.Throws<ConflictException>(() => MembershipRules.EnsureCanRemoveMember(
            actorUserId: Actor,
            targetUserId: Actor,
            actorRole: OrganizationRole.Owner,
            targetRole: OrganizationRole.Owner,
            remainingOwnerCount: 2));

        Assert.Contains("leave", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Admin_cannot_remove_an_owner()
    {
        Assert.Throws<ForbiddenException>(() => MembershipRules.EnsureCanRemoveMember(
            actorUserId: Actor,
            targetUserId: Target,
            actorRole: OrganizationRole.Admin,
            targetRole: OrganizationRole.Owner,
            remainingOwnerCount: 3));
    }

    [Fact]
    public void Member_cannot_remove_anyone()
    {
        Assert.Throws<ForbiddenException>(() => MembershipRules.EnsureCanRemoveMember(
            actorUserId: Actor,
            targetUserId: Target,
            actorRole: OrganizationRole.Member,
            targetRole: OrganizationRole.Viewer,
            remainingOwnerCount: 1));
    }

    [Fact]
    public void The_last_owner_cannot_leave()
    {
        Assert.Throws<ConflictException>(() =>
            MembershipRules.EnsureCanLeave(OrganizationRole.Owner, remainingOwnerCount: 1));
    }

    [Theory]
    [InlineData(OrganizationRole.Viewer)]
    [InlineData(OrganizationRole.Member)]
    [InlineData(OrganizationRole.Admin)]
    public void Any_non_owner_can_leave(OrganizationRole role) =>
        MembershipRules.EnsureCanLeave(role, remainingOwnerCount: 1);

    [Fact]
    public void An_owner_can_leave_when_another_owner_remains() =>
        MembershipRules.EnsureCanLeave(OrganizationRole.Owner, remainingOwnerCount: 2);
}
