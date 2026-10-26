using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workspace.Application.Abstractions;
using Workspace.Domain;

namespace Workspace.Application.Invitations;

/// <summary>Sends one invitation email.</summary>
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

        var invitation = await db.CrossTenant<Workspace.Domain.Entities.Invitation>()
            .Include(i => i.Organization)
            .Include(i => i.InvitedByUser)
            .FirstOrDefaultAsync(i => i.Id == payload.InvitationId, cancellationToken);

        if (invitation is null)
        {
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
