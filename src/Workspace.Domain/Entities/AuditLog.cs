using Workspace.Domain.Common;

namespace Workspace.Domain.Entities;

/// <summary>An append-only record of a security- or data-relevant action.</summary>
public class AuditLog : Entity
{
    /// <summary>Owning tenant, or <c>null</c> for account-level events.</summary>
    public Guid? OrganizationId { get; set; }

    public Guid? ActorUserId { get; set; }

    /// <summary>Actor's email at the time of the action.</summary>
    public string? ActorEmail { get; set; }

    /// <summary>Dotted action name, for eg <c>member.role_changed</c>.</summary>
    public required string Action { get; set; }

    /// <summary>Type of the affected resource, for eg <c>project</c>.</summary>
    public required string ResourceType { get; set; }

    public string? ResourceId { get; set; }

    /// <summary>Extra context as a JSON object (jsonb in Postgres).</summary>
    public string? Metadata { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>Correlates the entry w/ the HTTP request that produced it.</summary>
    public string? CorrelationId { get; set; }
}
