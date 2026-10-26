using Workspace.Domain.Common;

namespace Workspace.Domain.Authorization;

/// <summary>Membership invariants that a static permission matrix cant express, cuz they depend on the relationship btwn actor and target.</summary>
public static class MembershipRules
{
    /// <summary>Whether <paramref name="actorRole"/> may act on a membership currently held at <paramref name="targetRole"/>.</summary>
    public static bool CanActOnMemberWithRole(OrganizationRole actorRole, OrganizationRole targetRole) =>
        actorRole switch
        {
            OrganizationRole.Owner => true,
            OrganizationRole.Admin => targetRole != OrganizationRole.Owner,
            _ => false,
        };

    /// <summary>Whether <paramref name="actorRole"/> may grant <paramref name="roleToGrant"/>.</summary>
    public static bool CanGrantRole(OrganizationRole actorRole, OrganizationRole roleToGrant) =>
        actorRole switch
        {
            OrganizationRole.Owner => true,
            OrganizationRole.Admin => roleToGrant != OrganizationRole.Owner,
            _ => false,
        };

    /// <summary>Throws unless the actor may change a membership from <paramref name="currentRole"/> to <paramref name="newRole"/>.</summary>
    public static void EnsureCanChangeRole(
        Guid actorUserId,
        Guid targetUserId,
        OrganizationRole actorRole,
        OrganizationRole currentRole,
        OrganizationRole newRole,
        int remainingOwnerCount)
    {
        if (actorUserId == targetUserId)
        {
            throw new ForbiddenException("You cannot change your own role.");
        }

        if (!CanActOnMemberWithRole(actorRole, currentRole))
        {
            throw new ForbiddenException("You do not have permission to manage this member.");
        }

        if (!CanGrantRole(actorRole, newRole))
        {
            throw new ForbiddenException("You cannot grant a role higher than your own.");
        }

        if (currentRole == newRole)
        {
            throw new ConflictException("The member already has that role.");
        }

        EnsureOrganizationKeepsAnOwner(currentRole, newRole, remainingOwnerCount);
    }

    /// <summary>Throws unless the actor may remove the target's membership.</summary>
    public static void EnsureCanRemoveMember(
        Guid actorUserId,
        Guid targetUserId,
        OrganizationRole actorRole,
        OrganizationRole targetRole,
        int remainingOwnerCount)
    {
        if (actorUserId == targetUserId)
        {
            // leaving is a separate, intentional operation w/ its own rules.
            throw new ConflictException("Use the leave-organization endpoint to remove yourself.");
        }

        if (!CanActOnMemberWithRole(actorRole, targetRole))
        {
            throw new ForbiddenException("You do not have permission to remove this member.");
        }

        EnsureOrganizationKeepsAnOwner(targetRole, null, remainingOwnerCount);
    }

    /// <summary>Throws unless the user may leave the organization.</summary>
    public static void EnsureCanLeave(OrganizationRole role, int remainingOwnerCount) =>
        EnsureOrganizationKeepsAnOwner(role, null, remainingOwnerCount);

    /// <summary>Core invariant: an organization always has at least one owner.</summary>
    private static void EnsureOrganizationKeepsAnOwner(
        OrganizationRole currentRole,
        OrganizationRole? newRole,
        int ownerCount)
    {
        var losesAnOwner = currentRole == OrganizationRole.Owner && newRole != OrganizationRole.Owner;

        if (losesAnOwner && ownerCount <= 1)
        {
            throw new ConflictException(
                "An organization must keep at least one owner. Promote another member first.");
        }
    }
}
