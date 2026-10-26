using Microsoft.AspNetCore.Mvc;
using Workspace.Api.Http;
using Workspace.Application.Common;
using Workspace.Application.Invitations;
using Workspace.Domain;
using Workspace.Domain.Authorization;

namespace Workspace.Api.Endpoints;

public static class InvitationEndpoints
{
    /// <summary>Invitation management, inside an organization.</summary>
    public static RouteGroupBuilder MapInvitationEndpoints(this RouteGroupBuilder scoped)
    {
        var group = scoped.MapGroup("/invitations").WithTags("Invitations");

        group.MapGet("/", async (
                InvitationService invitations,
                CancellationToken cancellationToken,
                InvitationStatus? status = null,
                int? page = null,
                int? pageSize = null) =>
                Results.Ok(await invitations.ListAsync(new PageRequest(page, pageSize), status, cancellationToken)))
            .RequirePermission(Permission.InvitationRead)
            .WithSummary("List invitations")
            .WithDescription("Includes delivery state, so an admin can see that an email failed and resend it.")
            .Produces<PagedResult<InvitationDto>>();

        group.MapPost("/", async (
                CreateInvitationRequest request,
                InvitationService invitations,
                CancellationToken cancellationToken) =>
            {
                var invitation = await invitations.CreateAsync(request, cancellationToken);

                return Results.Created($"/api/invitations/{invitation.Id}", invitation);
            })
            .WithValidation<CreateInvitationRequest>()
            .RequirePermission(Permission.MemberInvite)
            .WithSummary("Invite someone by email")
            .WithDescription(
                "Returns as soon as the invitation is stored; the email is sent by the background " +
                "worker. The token is never returned by the API — it exists only in the email.")
            .Produces<InvitationDto>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapPost("/{invitationId:guid}/resend", async (
                Guid invitationId,
                InvitationService invitations,
                CancellationToken cancellationToken) =>
                Results.Ok(await invitations.ResendAsync(invitationId, cancellationToken)))
            .RequirePermission(Permission.MemberInvite)
            .WithSummary("Resend an invitation")
            .WithDescription("Issues a fresh token, so the previous link stops working.")
            .Produces<InvitationDto>();

        group.MapDelete("/{invitationId:guid}", async (
                Guid invitationId,
                InvitationService invitations,
                CancellationToken cancellationToken) =>
            {
                await invitations.RevokeAsync(invitationId, cancellationToken);

                return Results.NoContent();
            })
            .RequirePermission(Permission.InvitationRevoke)
            .WithSummary("Revoke a pending invitation")
            .WithDescription("Rotates the stored token hash, so a link already sitting in an inbox stops working.")
            .Produces(StatusCodes.Status204NoContent);

        return group;
    }

    /// <summary>Redemption routes, which cant be organization-scoped: the invitee has a token precisely cuz they have no membership yet.</summary>
    public static RouteGroupBuilder MapInvitationRedemptionEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/invitations").WithTags("Invitations");

        group.MapGet("/preview", async (
                string token,
                InvitationService invitations,
                CancellationToken cancellationToken) =>
                Results.Ok(await invitations.PreviewAsync(token, cancellationToken)))
            .RequireRateLimiting(RateLimitingOptions.InvitationPolicy)
            .WithSummary("Look up an invitation by token")
            .WithDescription(
                "Anonymous, so the accept screen can show who invited whom before sign-in. " +
                "Rate limited, and returns only the organization name, invited address and role.")
            .Produces<InvitationPreviewDto>()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("/accept", async (
                AcceptInvitationRequest request,
                InvitationService invitations,
                CancellationToken cancellationToken) =>
                Results.Ok(await invitations.AcceptAsync(request.Token, cancellationToken)))
            .RequireAuthorization()
            .WithValidation<AcceptInvitationRequest>()
            .WithSummary("Accept an invitation")
            .WithDescription(
                "The signed-in account's email must match the invited address, so a forwarded " +
                "link is useless to anyone else.")
            .Produces<AcceptInvitationResult>()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return group;
    }
}
