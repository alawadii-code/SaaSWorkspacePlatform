using Workspace.Domain.Common;

namespace Workspace.Domain.Entities;

/// <summary>
/// A unit of work inside a project.
/// </summary>
/// <remarks>
/// <see cref="OrganizationId"/> is denormalized from the parent project. It
/// costs one extra column and buys two things: the tenant query filter applies
/// uniformly to every tenant-owned table without a join, and a task can never
/// be re-parented into another tenant's project (the composite foreign key
/// makes that unrepresentable).
/// </remarks>
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

    /// <summary>
    /// Whether <paramref name="userId"/> is the creator or the assignee, which
    /// is what <c>TaskUpdateOwn</c> grants for roles without
    /// <c>TaskUpdateAny</c>.
    /// </summary>
    public bool IsOwnedBy(Guid userId) => CreatedByUserId == userId || AssigneeUserId == userId;
}
