namespace Workspace.Application.Abstractions;

/// <summary>
/// Records audit entries. Implementations add the row to the same change
/// tracker as the action being audited, so the caller's single
/// <see cref="IWorkspaceDb.SaveChangesAsync"/> commits both atomically — there
/// is no code path that changes data without its audit entry.
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Queues an audit entry for the resolved organization, attributed to the
    /// current caller.
    /// </summary>
    /// <param name="action">A constant from <c>AuditActions</c>.</param>
    /// <param name="resourceType">A constant from <c>AuditResources</c>.</param>
    /// <param name="resourceId">Identifier of the affected resource, if any.</param>
    /// <param name="metadata">
    /// Extra context, serialized to a JSON object. Must never contain
    /// passwords, hashes or tokens.
    /// </param>
    void Record(
        string action,
        string resourceType,
        string? resourceId = null,
        object? metadata = null);

    /// <summary>
    /// Queues an audit entry with an explicit organization and actor, for
    /// flows where no tenant is resolved: account-level events such as sign-in
    /// (<paramref name="organizationId"/> is <c>null</c>), and invitation
    /// acceptance, where membership does not exist yet.
    /// </summary>
    void RecordFor(
        Guid? organizationId,
        Guid? actorUserId,
        string? actorEmail,
        string action,
        string resourceType,
        string? resourceId = null,
        object? metadata = null);
}
