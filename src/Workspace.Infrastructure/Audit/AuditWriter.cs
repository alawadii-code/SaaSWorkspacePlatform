using System.Text.Json;
using Workspace.Application.Abstractions;
using Workspace.Domain.Entities;
using Workspace.Infrastructure.Persistence;

namespace Workspace.Infrastructure.Audit;

/// <summary>
/// Writes audit entries into the same change tracker as the action being
/// audited, so the caller's single <c>SaveChangesAsync</c> commits data and
/// trail in one transaction.
/// </summary>
public sealed class AuditWriter(
    WorkspaceDbContext db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IClock clock) : IAuditWriter
{
    private static readonly JsonSerializerOptions MetadataOptions = new(JsonSerializerDefaults.Web);

    public void Record(string action, string resourceType, string? resourceId = null, object? metadata = null)
    {
        if (!tenant.IsResolved)
        {
            throw new InvalidOperationException(
                $"Cannot record '{action}' without a resolved organization. Use RecordFor instead.");
        }

        RecordFor(
            tenant.OrganizationId,
            currentUser.UserId ?? tenant.UserId,
            currentUser.Email,
            action,
            resourceType,
            resourceId,
            metadata);
    }

    public void RecordFor(
        Guid? organizationId,
        Guid? actorUserId,
        string? actorEmail,
        string action,
        string resourceType,
        string? resourceId = null,
        object? metadata = null)
    {
        var now = clock.UtcNow;

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            ActorEmail = actorEmail,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Metadata = Serialize(metadata),
            IpAddress = currentUser.IpAddress,
            UserAgent = currentUser.UserAgent,
            CorrelationId = currentUser.CorrelationId,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    /// <summary>
    /// Serializes metadata to a JSON object. Anything that fails to serialize
    /// is dropped rather than allowed to fail the request it belongs to: an
    /// audit entry with less context is better than a lost user action.
    /// </summary>
    private static string? Serialize(object? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Serialize(metadata, MetadataOptions);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
