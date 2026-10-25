using Workspace.Domain.Common;

namespace Workspace.Domain.Entities;

/// <summary>A container for tasks, owned by one organization.</summary>
public class Project : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }

    public required string Name { get; set; }

    /// <summary>URL-safe handle, unique within the organization.</summary>
    public required string Slug { get; set; }

    public string? Description { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public Organization? Organization { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = [];
}
