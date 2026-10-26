using Workspace.Domain.Common;

namespace Workspace.Domain.Entities;

/// <summary>A tenant.</summary>
public class Organization : Entity
{
    public required string Name { get; set; }

    /// <summary>URL-safe unique handle, used in client routes.</summary>
    public required string Slug { get; set; }

    public Guid CreatedByUserId { get; set; }

    public ICollection<Membership> Memberships { get; set; } = [];

    public ICollection<Project> Projects { get; set; } = [];

    public ICollection<Invitation> Invitations { get; set; } = [];
}
