namespace Workspace.Domain;

/// <summary>Lifecycle of an organization invitation.</summary>
public enum InvitationStatus
{
    Pending = 1,
    Accepted = 2,
    Revoked = 3,
    Expired = 4,
}

/// <summary>Lifecycle of a project.</summary>
public enum ProjectStatus
{
    Active = 1,
    Archived = 2,
}

/// <summary>Workflow state of a task.</summary>
public enum TaskState
{
    Todo = 1,
    InProgress = 2,
    Blocked = 3,
    Done = 4,
}

/// <summary>Task priority, ordered low to high.</summary>
public enum TaskPriority
{
    Low = 10,
    Medium = 20,
    High = 30,
    Urgent = 40,
}

/// <summary>Delivery state of a queued invitation email.</summary>
public enum InvitationDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
}
