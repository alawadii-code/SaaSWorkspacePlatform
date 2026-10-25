using Microsoft.AspNetCore.Mvc;
using Workspace.Api.Http;
using Workspace.Application.Common;
using Workspace.Application.Members;
using Workspace.Domain.Authorization;

namespace Workspace.Api.Endpoints;

public static class MemberEndpoints
{
    public static RouteGroupBuilder MapMemberEndpoints(this RouteGroupBuilder scoped)
    {
        var group = scoped.MapGroup("/members").WithTags("Members");

        group.MapGet("/", async (
                MembershipService memberships,
                CancellationToken cancellationToken,
                int? page = null,
                int? pageSize = null,
                string? search = null) =>
                Results.Ok(await memberships.ListAsync(new PageRequest(page, pageSize), search, cancellationToken)))
            .RequirePermission(Permission.MemberRead)
            .WithSummary("List members")
            .Produces<PagedResult<MemberDto>>();

        group.MapPatch("/{userId:guid}/role", async (
                Guid userId,
                ChangeMemberRoleRequest request,
                MembershipService memberships,
                CancellationToken cancellationToken) =>
                Results.Ok(await memberships.ChangeRoleAsync(userId, request, cancellationToken)))
            .WithValidation<ChangeMemberRoleRequest>()
            .RequirePermission(Permission.MemberRoleUpdate)
            .WithSummary("Change a member's role")
            .WithDescription(
                "Admins may manage members, viewers and other admins, but not owners, and may " +
                "not grant Owner. Nobody may change their own role, and the last owner cannot " +
                "be demoted.")
            .Produces<MemberDto>()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapDelete("/{userId:guid}", async (
                Guid userId,
                MembershipService memberships,
                CancellationToken cancellationToken) =>
            {
                await memberships.RemoveAsync(userId, cancellationToken);

                return Results.NoContent();
            })
            .RequirePermission(Permission.MemberRemove)
            .WithSummary("Remove a member")
            .WithDescription("Also clears that member's task assignments, so no task is left assigned to a non-member.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapPost("/me/leave", async (
                MembershipService memberships,
                CancellationToken cancellationToken) =>
            {
                await memberships.LeaveAsync(cancellationToken);

                return Results.NoContent();
            })
            .WithSummary("Leave the organization")
            .WithDescription(
                "Available to every role — no permission needed to remove yourself — except to " +
                "the last owner, who must promote someone else first.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return group;
    }
}
