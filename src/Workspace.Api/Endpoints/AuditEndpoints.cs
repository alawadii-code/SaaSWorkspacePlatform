using Workspace.Api.Http;
using Workspace.Application.Audit;
using Workspace.Application.Common;
using Workspace.Domain.Authorization;

namespace Workspace.Api.Endpoints;

public static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder scoped)
    {
        var group = scoped.MapGroup("/audit-logs").WithTags("Audit");

        group.MapGet("/", async (
                AuditQueryService audit,
                CancellationToken cancellationToken,
                string? action = null,
                string? resourceType = null,
                Guid? actorUserId = null,
                DateTimeOffset? from = null,
                DateTimeOffset? to = null,
                int? page = null,
                int? pageSize = null) =>
                Results.Ok(await audit.ListAsync(
                    new PageRequest(page, pageSize),
                    new AuditLogQuery(action, resourceType, actorUserId, from, to),
                    cancellationToken)))
            .RequirePermission(Permission.AuditRead)
            .WithSummary("Read the organization's audit trail")
            .WithDescription(
                "Read-only: there is no API path that writes, edits or deletes an entry. " +
                "Entries are written in the same transaction as the action they record, and the " +
                "table rejects UPDATE and DELETE at the database level.")
            .Produces<PagedResult<AuditLogDto>>();

        group.MapGet("/actions", async (AuditQueryService audit, CancellationToken cancellationToken) =>
                Results.Ok(await audit.ListActionsAsync(cancellationToken)))
            .RequirePermission(Permission.AuditRead)
            .WithSummary("Distinct action names present in this organization's trail")
            .Produces<IReadOnlyList<string>>();

        return group;
    }
}
