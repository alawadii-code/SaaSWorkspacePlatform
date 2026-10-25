using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workspace.Application.Abstractions;
using Workspace.Domain;

namespace Workspace.Application.Invitations;

/// <summary>
/// Sends one invitation email. Runs in the worker process, one scope per job.
/// </summary>
/// <remarks>
/// Failures update the invitation's delivery state and then rethrow, so the
/// worker's retry and dead-letter policy decides what happens next while the
/// invitation row still shows operators what went wrong.
/// </remarks>
public sealed class SendInvitationEmailHandler(
    IWorkspaceDb db,
    IEmailSender emailSender,
    IClock clock,
    IOptions<InvitationOptions> options,
    ILogger<SendInvitationEmailHandler> logger) : IJobHandler
{
    public string JobType => JobTypes.SendInvitationEmail;

    public async Task HandleAsync(JobMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SendInvitationEmailPayload>(message.Payload)
            ?? throw new InvalidOperationException($"Job {message.Id} has an unreadable payload.");

        // The worker has no tenant context, so this read is cross-tenant by
        // necessity and constrained to the invitation id carried by the job.
        var invitation = await db.CrossTenant<Workspace.Domain.Entities.Invitation>()
            .Include(i => i.Organization)
            .Include(i => i.InvitedByUser)
            .FirstOrDefaultAsync(i => i.Id == payload.InvitationId, cancellationToken);

        if (invitation is null)
        {
            // The organization or invitation was deleted after the job was
            // queued. Nothing to send and nothing to retry.
            logger.LogWarning("Invitation {InvitationId} no longer exists; dropping job.", payload.InvitationId);
            return;
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            logger.LogInformation(
                "Invitation {InvitationId} is {Status}; skipping email.",
                invitation.Id,
                invitation.Status);
            return;
        }

        var email = InvitationEmail.Build(
            recipientEmail: invitation.Email,
            organizationName: invitation.Organization?.Name ?? "your workspace",
            invitedByName: invitation.InvitedByUser?.DisplayName ?? "A workspace admin",
            role: invitation.Role,
            acceptUrl: options.Value.BuildAcceptUrl(payload.Token),
            expiresAt: invitation.ExpiresAt);

        var now = clock.UtcNow;

        try
        {
            await emailSender.SendAsync(email, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            invitation.DeliveryAttempts = message.Attempt + 1;
            invitation.DeliveryStatus = InvitationDeliveryStatus.Failed;

            // Store the exception type and message only — never a stack trace
            // or credentials from a connection string.
            invitation.DeliveryError = $"{ex.GetType().Name}: {Truncate(ex.Message, 400)}";
            invitation.UpdatedAt = now;

            await db.SaveChangesAsync(CancellationToken.None);

            throw;
        }

        invitation.DeliveryStatus = InvitationDeliveryStatus.Sent;
        invitation.DeliveredAt = now;
        invitation.DeliveryAttempts = message.Attempt + 1;
        invitation.DeliveryError = null;
        invitation.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Sent invitation {InvitationId} to {Email} (attempt {Attempt}).",
            invitation.Id,
            invitation.Email,
            invitation.DeliveryAttempts);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
