using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Workspace.Application.Abstractions;
using Workspace.Application.Common;
using Workspace.Domain.Authorization;

namespace Workspace.Application.Audit;

/// <summary>Read-only access to the current organization's audit trail.</summary>
public sealed class AuditQueryService(IWorkspaceDb db, ITenantContext tenant)
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(
        PageRequest page,
        AuditLogQuery filters,
        CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.AuditRead);

        var query = db.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters.Action))
        {
            var action = filters.Action.Trim();
            query = query.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(filters.ResourceType))
        {
            var resourceType = filters.ResourceType.Trim();
            query = query.Where(a => a.ResourceType == resourceType);
        }

        if (filters.ActorUserId is not null)
        {
            query = query.Where(a => a.ActorUserId == filters.ActorUserId);
        }

        if (filters.From is not null)
        {
            query = query.Where(a => a.CreatedAt >= filters.From);
        }

        if (filters.To is not null)
        {
            query = query.Where(a => a.CreatedAt <= filters.To);
        }

        return await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .ToPagedResultAsync(
                page,
                a => new AuditLogDto(
                    a.Id,
                    a.Action,
                    a.ResourceType,
                    a.ResourceId,
                    a.ActorUserId,
                    a.ActorEmail,
                    ParseMetadata(a.Metadata),
                    a.IpAddress,
                    a.CorrelationId,
                    a.CreatedAt),
                cancellationToken);
    }

    /// <summary>Returns the distinct actions present in this organization's trail, for the filter dropdown.</summary>
    public async Task<IReadOnlyList<string>> ListActionsAsync(CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.AuditRead);

        return await db.AuditLogs
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(action => action)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Metadata is stored as jsonb.</summary>
    private static JsonElement? ParseMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(metadata);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
