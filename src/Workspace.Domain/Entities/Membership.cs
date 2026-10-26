using Workspace.Domain.Authorization;
using Workspace.Domain.Common;

namespace Workspace.Domain.Entities;

/// <summary>Joins a user to an organization with exactly one role.</summary>
public class Membership : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }

    public Guid UserId { get; set; }

    public OrganizationRole Role { get; set; }

    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>Invitation this membership came from, when it was not a founding membership.</summary>
    public Guid? InvitationId { get; set; }

    public Organization? Organization { get; set; }

    public User? User { get; set; }
}
