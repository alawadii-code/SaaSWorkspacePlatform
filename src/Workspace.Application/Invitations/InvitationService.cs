using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workspace.Application.Abstractions;
using Workspace.Application.Common;
using Workspace.Domain;
using Workspace.Domain.Audit;
using Workspace.Domain.Authorization;
using Workspace.Domain.Common;
using Workspace.Domain.Entities;

namespace Workspace.Application.Invitations;

/// <summary>Invitation lifecycle: create, list, revoke, resend, preview and accept, plus the two reconciliation passes the worker runs.</summary>
public sealed class InvitationService(
    IWorkspaceDb db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    ITokenService tokens,
    IJobQueue queue,
    IAuditWriter audit,
    IClock clock,
    IOptions<InvitationOptions> options,
    ILogger<InvitationService> logger)
{
    private readonly InvitationOptions invitationOptions = options.Value;

    public async Task<InvitationDto> CreateAsync(
        CreateInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.MemberInvite);

        if (!MembershipRules.CanGrantRole(tenant.Role, request.Role))
        {
            throw new ForbiddenException("You cannot invite someone at a role higher than your own.");
        }

        var email = User.Normalize(request.Email);
        var now = clock.UtcNow;

        var alreadyMember = await db.Memberships
            .AnyAsync(m => m.User!.NormalizedEmail == email, cancellationToken);

        if (alreadyMember)
        {
            throw new ConflictException("That person is already a member of this organization.");
        }

        var hasPending = await db.Invitations
            .AnyAsync(
                i => i.Email == email && i.Status == InvitationStatus.Pending && i.ExpiresAt > now,
                cancellationToken);

        if (hasPending)
        {
            throw new ConflictException(
                "An invitation is already pending for that address. Resend or revoke it instead.");
        }

        var (plaintext, hash) = tokens.CreateOpaqueToken();

        var invitation = new Invitation
        {
            OrganizationId = tenant.OrganizationId,
            Email = email,
            Role = request.Role,
            TokenHash = hash,
            Status = InvitationStatus.Pending,
            ExpiresAt = now.Add(invitationOptions.Lifetime),
            InvitedByUserId = tenant.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Invitations.Add(invitation);

        audit.Record(
            AuditActions.MemberInvited,
            AuditResources.Invitation,
            invitation.Id.ToString(),
            new { email, role = request.Role.ToString() });

        await db.SaveChangesAsync(cancellationToken);

        // Queued after the commit so the worker cant pick up an invitation thatz not
        // there yet. A failed enqueue is recovered by ReconcileUndeliveredAsync.
        await EnqueueEmailAsync(invitation.Id, plaintext, cancellationToken);

        return ToDto(invitation);
    }

    public async Task<PagedResult<InvitationDto>> ListAsync(
        PageRequest page,
        InvitationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.InvitationRead);

        var query = db.Invitations.AsQueryable();

        if (status is not null)
        {
            query = query.Where(i => i.Status == status);
        }

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .ToPagedResultAsync(page, ToDto, cancellationToken);
    }

    public async Task RevokeAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.InvitationRevoke);

        var invitation = await db.Invitations
            .FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken)
            ?? throw new NotFoundException("Invitation");

        if (invitation.Status != InvitationStatus.Pending)
        {
            throw new ConflictException("Only a pending invitation can be revoked.");
        }

        var now = clock.UtcNow;

        invitation.Status = InvitationStatus.Revoked;
        invitation.RevokedAt = now;
        invitation.UpdatedAt = now;

        invitation.TokenHash = tokens.CreateOpaqueToken().Hash;

        audit.Record(
            AuditActions.MemberInvitationRevoked,
            AuditResources.Invitation,
            invitation.Id.ToString(),
            new { email = invitation.Email });

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Issues a fresh token for a pending invitation and queues the email again.</summary>
    public async Task<InvitationDto> ResendAsync(Guid invitationId, CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.MemberInvite);

        var invitation = await db.Invitations
            .FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken)
            ?? throw new NotFoundException("Invitation");

        if (invitation.Status != InvitationStatus.Pending)
        {
            throw new ConflictException("Only a pending invitation can be resent.");
        }

        var now = clock.UtcNow;
        var (plaintext, hash) = tokens.CreateOpaqueToken();

        invitation.TokenHash = hash;
        invitation.ExpiresAt = now.Add(invitationOptions.Lifetime);
        invitation.DeliveryStatus = InvitationDeliveryStatus.Pending;
        invitation.DeliveryError = null;
        invitation.UpdatedAt = now;

        audit.Record(
            AuditActions.MemberInvited,
            AuditResources.Invitation,
            invitation.Id.ToString(),
            new { email = invitation.Email, resent = true });

        await db.SaveChangesAsync(cancellationToken);

        await EnqueueEmailAsync(invitation.Id, plaintext, cancellationToken);

        return ToDto(invitation);
    }

    /// <summary>What the accept screen shows before the invitee has signed in.</summary>
    public async Task<InvitationPreviewDto> PreviewAsync(string token, CancellationToken cancellationToken = default)
    {
        var invitation = await FindByTokenAsync(token, cancellationToken)
            ?? throw new NotFoundException("Invitation");

        var now = clock.UtcNow;

        var reason = invitation.Status switch
        {
            InvitationStatus.Accepted => "This invitation has already been accepted.",
            InvitationStatus.Revoked => "This invitation has been revoked.",
            _ when invitation.ExpiresAt <= now => "This invitation has expired.",
            _ => null,
        };

        return new InvitationPreviewDto(
            invitation.Organization?.Name ?? "Unknown organization",
            invitation.Email,
            invitation.Role,
            invitation.ExpiresAt,
            reason is null,
            reason);
    }

    /// <summary>Redeems an invitation for the signed-in caller and creates their membership.</summary>
    public async Task<AcceptInvitationResult> AcceptAsync(string token, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var invitation = await FindByTokenAsync(token, cancellationToken)
            ?? throw new NotFoundException("Invitation");

        if (!invitation.IsRedeemable(now))
        {
            throw new ConflictException("This invitation is no longer valid.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new UnauthorizedException();

        if (!string.Equals(user.NormalizedEmail, invitation.Email, StringComparison.Ordinal))
        {
            throw new ForbiddenException("This invitation was sent to a different email address.");
        }

        var existing = await db.CrossTenant<Membership>()
            .FirstOrDefaultAsync(
                m => m.OrganizationId == invitation.OrganizationId && m.UserId == userId,
                cancellationToken);

        if (existing is null)
        {
            db.Memberships.Add(new Membership
            {
                OrganizationId = invitation.OrganizationId,
                UserId = userId,
                Role = invitation.Role,
                JoinedAt = now,
                InvitationId = invitation.Id,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        invitation.Status = InvitationStatus.Accepted;
        invitation.AcceptedAt = now;
        invitation.AcceptedByUserId = userId;
        invitation.UpdatedAt = now;

        audit.RecordFor(
            invitation.OrganizationId,
            userId,
            user.Email,
            AuditActions.MemberInvitationAccepted,
            AuditResources.Invitation,
            invitation.Id.ToString(),
            new { role = invitation.Role.ToString() });

        await db.SaveChangesAsync(cancellationToken);

        return new AcceptInvitationResult(
            invitation.OrganizationId,
            invitation.Organization?.Name ?? string.Empty,
            invitation.Organization?.Slug ?? string.Empty,
            invitation.Role);
    }

    /// <summary>Worker pass: marks pending invitations whose expiry has passed, so the member list stops offering to resend links that cant be redeemed.</summary>
    public async Task<int> ExpireStaleAsync(int batchSize = 200, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        var stale = await db.CrossTenant<Invitation>()
            .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt <= now)
            .OrderBy(i => i.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var invitation in stale)
        {
            invitation.Status = InvitationStatus.Expired;
            invitation.UpdatedAt = now;
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return stale.Count;
    }

    /// <summary>Worker pass: finds pending invitations whose email was nvr queued and queues it.</summary>
    public async Task<int> ReconcileUndeliveredAsync(int batchSize = 50, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var cutoff = now.Subtract(invitationOptions.ReconcileAfter);

        var undelivered = await db.CrossTenant<Invitation>()
            .Where(i =>
                i.Status == InvitationStatus.Pending &&
                i.DeliveryStatus == InvitationDeliveryStatus.Pending &&
                i.DeliveryAttempts == 0 &&
                i.ExpiresAt > now &&
                i.UpdatedAt <= cutoff)
            .OrderBy(i => i.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (undelivered.Count == 0)
        {
            return 0;
        }

        var queued = new List<(Guid Id, string Token)>(undelivered.Count);

        foreach (var invitation in undelivered)
        {
            var (plaintext, hash) = tokens.CreateOpaqueToken();
            invitation.TokenHash = hash;
            invitation.UpdatedAt = now;
            queued.Add((invitation.Id, plaintext));
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var (id, plaintext) in queued)
        {
            await EnqueueEmailAsync(id, plaintext, cancellationToken);
        }

        logger.LogInformation("Re-queued {Count} undelivered invitation emails.", queued.Count);

        return queued.Count;
    }

    /// <summary>Looks up an invitation by the hash of the supplied token.</summary>
    private async Task<Invitation?> FindByTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ValidationFailedException("token", "An invitation token is required.");
        }

        var hash = tokens.HashToken(token);

        return await db.CrossTenant<Invitation>()
            .Include(i => i.Organization)
            .FirstOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
    }

    private async Task EnqueueEmailAsync(Guid invitationId, string token, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new SendInvitationEmailPayload(invitationId, token));
        var message = JobMessage.Create(JobTypes.SendInvitationEmail, payload, clock.UtcNow);

        try
        {
            await queue.EnqueueAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Failed to enqueue invitation email for {InvitationId}; reconciliation will retry.",
                invitationId);
        }
    }

    private static InvitationDto ToDto(Invitation invitation) =>
        new(
            invitation.Id,
            invitation.Email,
            invitation.Role,
            invitation.Status,
            invitation.DeliveryStatus,
            invitation.DeliveryAttempts,
            invitation.DeliveryError,
            invitation.ExpiresAt,
            invitation.CreatedAt,
            invitation.AcceptedAt);
}
