using Microsoft.EntityFrameworkCore;
using Workspace.Application.Abstractions;
using Workspace.Application.Common;
using Workspace.Domain.Audit;
using Workspace.Domain.Authorization;
using Workspace.Domain.Common;
using Workspace.Domain.Entities;

namespace Workspace.Application.Members;

/// <summary>Reads and changes memberships of the organization resolved for this request.</summary>
public sealed class MembershipService(
    IWorkspaceDb db,
    ITenantContext tenant,
    IAuditWriter audit,
    IClock clock)
{
    public async Task<PagedResult<MemberDto>> ListAsync(
        PageRequest page,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.MemberRead);

        var callerId = tenant.UserId;

        // tenant-filtered set: this can only ever see the resolved organization.
        var query = db.Memberships
            .Include(m => m.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();

            query = query.Where(m =>
                m.User!.DisplayName.ToLower().Contains(term) ||
                m.User!.Email.ToLower().Contains(term));
        }

        return await query
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.User!.DisplayName)
            .ToPagedResultAsync(
                page,
                m => ToDto(m, callerId),
                cancellationToken);
    }

    public async Task<MemberDto> ChangeRoleAsync(
        Guid targetUserId,
        ChangeMemberRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.MemberRoleUpdate);

        var membership = await GetMembershipAsync(targetUserId, cancellationToken);
        var ownerCount = await CountOwnersAsync(cancellationToken);

        MembershipRules.EnsureCanChangeRole(
            actorUserId: tenant.UserId,
            targetUserId: targetUserId,
            actorRole: tenant.Role,
            currentRole: membership.Role,
            newRole: request.Role,
            remainingOwnerCount: ownerCount);

        var previousRole = membership.Role;
        membership.Role = request.Role;
        membership.UpdatedAt = clock.UtcNow;

        audit.Record(
            AuditActions.MemberRoleChanged,
            AuditResources.Membership,
            membership.Id.ToString(),
            new
            {
                targetUserId,
                from = previousRole.ToString(),
                to = request.Role.ToString(),
            });

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(membership, tenant.UserId);
    }

    public async Task RemoveAsync(Guid targetUserId, CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.MemberRemove);

        var membership = await GetMembershipAsync(targetUserId, cancellationToken);
        var ownerCount = await CountOwnersAsync(cancellationToken);

        MembershipRules.EnsureCanRemoveMember(
            actorUserId: tenant.UserId,
            targetUserId: targetUserId,
            actorRole: tenant.Role,
            targetRole: membership.Role,
            remainingOwnerCount: ownerCount);

        await UnassignTasksAsync(targetUserId, cancellationToken);

        db.Memberships.Remove(membership);

        audit.Record(
            AuditActions.MemberRemoved,
            AuditResources.Membership,
            membership.Id.ToString(),
            new { targetUserId, role = membership.Role.ToString() });

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Removes the caller's own membership.</summary>
    public async Task LeaveAsync(CancellationToken cancellationToken = default)
    {
        var membership = await GetMembershipAsync(tenant.UserId, cancellationToken);
        var ownerCount = await CountOwnersAsync(cancellationToken);

        MembershipRules.EnsureCanLeave(membership.Role, ownerCount);

        await UnassignTasksAsync(tenant.UserId, cancellationToken);

        db.Memberships.Remove(membership);

        audit.Record(
            AuditActions.MemberLeft,
            AuditResources.Membership,
            membership.Id.ToString(),
            new { role = membership.Role.ToString() });

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Clears a departing user's task assignments, so no task is left assigned to someone who can no longer see it.</summary>
    private async Task UnassignTasksAsync(Guid userId, CancellationToken cancellationToken)
    {
        var assigned = await db.Tasks
            .Where(t => t.AssigneeUserId == userId)
            .ToListAsync(cancellationToken);

        var now = clock.UtcNow;

        foreach (var task in assigned)
        {
            task.AssigneeUserId = null;
            task.UpdatedAt = now;
        }
    }

    private async Task<Membership> GetMembershipAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Memberships
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("Member");

    private Task<int> CountOwnersAsync(CancellationToken cancellationToken) =>
        db.Memberships.CountAsync(m => m.Role == OrganizationRole.Owner, cancellationToken);

    private static MemberDto ToDto(Membership membership, Guid callerId) =>
        new(
            membership.Id,
            membership.UserId,
            membership.User?.Email ?? string.Empty,
            membership.User?.DisplayName ?? string.Empty,
            membership.Role,
            membership.JoinedAt,
            membership.UserId == callerId);
}
