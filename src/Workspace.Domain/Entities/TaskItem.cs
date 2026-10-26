using Workspace.Domain.Common;

namespace Workspace.Domain.Entities;

/// <summary>A unit of work inside a project.</summary>
public class TaskItem : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }

    public Guid ProjectId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public TaskState Status { get; set; } = TaskState.Todo;

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    /// <summary>Assignee, who must be an active member of the same organization.</summary>
    public Guid? AssigneeUserId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset? DueAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public Project? Project { get; set; }

    public User? Assignee { get; set; }

    /// <summary>Whether <paramref name="userId"/> is the creator or the assignee, which is what <c>TaskUpdateOwn</c> grants for roles without <c>TaskUpdateAny</c>.</summary>
    public bool IsOwnedBy(Guid userId) => CreatedByUserId == userId || AssigneeUserId == userId;
}
