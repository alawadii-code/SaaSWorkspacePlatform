using Workspace.Domain.Authorization;
using Workspace.Domain.Common;

namespace Workspace.Domain.Entities;

/// <summary>A pending offer of membership, sent by email and redeemed with a token.</summary>
public class Invitation : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }

    /// <summary>Lower-cased invitee address.</summary>
    public required string Email { get; set; }

    public OrganizationRole Role { get; set; }

    /// <summary>Base64 SHA-256 hash of the single-use token.</summary>
    public required string TokenHash { get; set; }

    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid InvitedByUserId { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public Guid? AcceptedByUserId { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public InvitationDeliveryStatus DeliveryStatus { get; set; } = InvitationDeliveryStatus.Pending;

    public DateTimeOffset? DeliveredAt { get; set; }

    public int DeliveryAttempts { get; set; }

    public string? DeliveryError { get; set; }

    public Organization? Organization { get; set; }

    public User? InvitedByUser { get; set; }

    /// <summary>Whether the invitation can still be redeemed at <paramref name="now"/>.</summary>
    public bool IsRedeemable(DateTimeOffset now) =>
        Status == InvitationStatus.Pending && ExpiresAt > now;
}
