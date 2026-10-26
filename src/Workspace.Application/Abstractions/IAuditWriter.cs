namespace Workspace.Application.Abstractions;

/// <summary>Records audit entries.</summary>
public interface IAuditWriter
{
    /// <summary>Queues an audit entry for the resolved organization, attributed to the current caller.</summary>
    void Record(
        string action,
        string resourceType,
        string? resourceId = null,
        object? metadata = null);

    /// <summary>Queues an audit entry w/ an explicit organization and actor, for flows where no tenant is resolved: account-level events such as sign-in (<paramref name="organizationId"/> is <c>null</c>), and invitation acceptance, where membership does not exist yet.</summary>
    void RecordFor(
        Guid? organizationId,
        Guid? actorUserId,
        string? actorEmail,
        string action,
        string resourceType,
        string? resourceId = null,
        object? metadata = null);
}
