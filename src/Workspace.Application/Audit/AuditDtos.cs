using System.Text.Json;

namespace Workspace.Application.Audit;

public sealed record AuditLogDto(
    Guid Id,
    string Action,
    string ResourceType,
    string? ResourceId,
    Guid? ActorUserId,
    string? ActorEmail,
    JsonElement? Metadata,
    string? IpAddress,
    string? CorrelationId,
    DateTimeOffset CreatedAt);

/// <summary>Filters for the audit log endpoint; every field is optional.</summary>
public sealed record AuditLogQuery(
    string? Action = null,
    string? ResourceType = null,
    Guid? ActorUserId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);
