using Workspace.Domain.Common;

namespace Workspace.Domain.Entities;

/// <summary>
/// An append-only record of a security- or data-relevant action.
/// </summary>
/// <remarks>
/// <para>
/// Rows are written in the same database transaction as the change they
/// describe, so an action and its audit entry either both land or neither
/// does. A database trigger (migration <c>AuditImmutability</c>) rejects
/// UPDATE and DELETE on this table, so neither application code nor a
/// compromised database account can quietly rewrite history.
/// </para>
/// <para>
/// <see cref="OrganizationId"/> is nullable and this type deliberately does
/// not implement <see cref="ITenantOwned"/>: account-level events (login,
/// failed login, refresh-token replay) belong to no tenant. They are recorded
/// for operators but are never returned by the tenant-scoped audit endpoint,
/// because its filter requires an exact organization match and null never
/// matches.
/// </para>
/// <para>
/// The actor's email is denormalized so the trail stays readable after a user
/// account is deleted.
/// </para>
/// </remarks>
public class AuditLog : Entity
{
    /// <summary>Owning tenant, or <c>null</c> for account-level events.</summary>
    public Guid? OrganizationId { get; set; }

    public Guid? ActorUserId { get; set; }

    /// <summary>Actor's email at the time of the action.</summary>
    public string? ActorEmail { get; set; }

    /// <summary>Dotted action name, for example <c>member.role_changed</c>.</summary>
    public required string Action { get; set; }

    /// <summary>Type of the affected resource, for example <c>project</c>.</summary>
    public required string ResourceType { get; set; }

    public string? ResourceId { get; set; }

    /// <summary>
    /// Extra context as a JSON object (jsonb in Postgres). Must never contain
    /// credentials, tokens or password hashes.
    /// </summary>
    public string? Metadata { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>Correlates the entry with the HTTP request that produced it.</summary>
    public string? CorrelationId { get; set; }
}
