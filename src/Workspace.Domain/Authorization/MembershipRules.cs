using Workspace.Domain.Common;

namespace Workspace.Domain.Authorization;

/// <summary>
/// Membership invariants that a static permission matrix cannot express,
/// because they depend on the relationship between actor and target.
/// </summary>
/// <remarks>
/// Pure functions, unit-tested exhaustively. They are enforced server-side in
/// the membership and invitation services; the frontend only mirrors them.
/// </remarks>
public static class MembershipRules
{
    /// <summary>
    /// Whether <paramref name="actorRole"/> may act on a membership currently
    /// held at <paramref name="targetRole"/>.
    /// </summary>
    /// <remarks>
    /// An admin may manage members, viewers and other admins, but not owners:
    /// otherwise an admin could demote every owner and take over the tenant.
    /// </remarks>
    public static bool CanActOnMemberWithRole(OrganizationRole actorRole, OrganizationRole targetRole) =>
        actorRole switch
        {
            OrganizationRole.Owner => true,
            OrganizationRole.Admin => targetRole != OrganizationRole.Owner,
            _ => false,
        };

    /// <summary>
    /// Whether <paramref name="actorRole"/> may grant <paramref name="roleToGrant"/>.
    /// Only an owner can create another owner, so privilege can never be
    /// escalated above the actor's own level.
    /// </summary>
    public static bool CanGrantRole(OrganizationRole actorRole, OrganizationRole roleToGrant) =>
        actorRole switch
        {
            OrganizationRole.Owner => true,
            OrganizationRole.Admin => roleToGrant != OrganizationRole.Owner,
            _ => false,
        };

    /// <summary>
    /// Throws unless the actor may change a membership from
    /// <paramref name="currentRole"/> to <paramref name="newRole"/>.
    /// </summary>
    /// <param name="actorUserId">The user performing the change.</param>
    /// <param name="targetUserId">The user whose membership is changing.</param>
    /// <param name="actorRole">The actor's role in this organization.</param>
    /// <param name="currentRole">The target's current role.</param>
    /// <param name="newRole">The requested role.</param>
    /// <param name="remainingOwnerCount">
    /// How many owners the organization has, including the target.
    /// </param>
    public static void EnsureCanChangeRole(
        Guid actorUserId,
        Guid targetUserId,
        OrganizationRole actorRole,
        OrganizationRole currentRole,
        OrganizationRole newRole,
        int remainingOwnerCount)
    {
        // No self-service privilege changes, in either direction. An owner who
        // wants to step down promotes someone else first.
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

    /// <summary>
    /// Throws unless the actor may remove the target's membership.
    /// </summary>
    public static void EnsureCanRemoveMember(
        Guid actorUserId,
        Guid targetUserId,
        OrganizationRole actorRole,
        OrganizationRole targetRole,
        int remainingOwnerCount)
    {
        if (actorUserId == targetUserId)
        {
            // Leaving is a separate, intentional operation with its own rules.
            throw new ConflictException("Use the leave-organization endpoint to remove yourself.");
        }

        if (!CanActOnMemberWithRole(actorRole, targetRole))
        {
            throw new ForbiddenException("You do not have permission to remove this member.");
        }

        EnsureOrganizationKeepsAnOwner(targetRole, null, remainingOwnerCount);
    }

    /// <summary>
    /// Throws unless the user may leave the organization. The last owner may
    /// not leave, because an organization without an owner cannot be
    /// administered or deleted by anyone.
    /// </summary>
    public static void EnsureCanLeave(OrganizationRole role, int remainingOwnerCount) =>
        EnsureOrganizationKeepsAnOwner(role, null, remainingOwnerCount);

    /// <summary>
    /// Core invariant: an organization always has at least one owner.
    /// </summary>
    /// <param name="currentRole">Role being changed or removed.</param>
    /// <param name="newRole">New role, or <c>null</c> when the membership is going away.</param>
    /// <param name="ownerCount">Owner count including the membership in question.</param>
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
