using Microsoft.AspNetCore.Mvc;
using Workspace.Api.Http;
using Workspace.Application.Auth;
using Workspace.Application.Organizations;
using Workspace.Domain.Authorization;

namespace Workspace.Api.Endpoints;

public static class OrganizationEndpoints
{
    /// <summary>Maps the organization routes, and the tenant-scoped group evry other resource hangs off.</summary>
    public static RouteGroupBuilder MapOrganizationEndpoints(this RouteGroupBuilder api)
    {
        var unscopedGroup = api
            .MapGroup("/organizations")
            .RequireAuthorization()
            .WithTags("Organizations");

        unscopedGroup.MapPost("/", async (
                CreateOrganizationRequest request,
                OrganizationService organizations,
                CancellationToken cancellationToken) =>
            {
                var created = await organizations.CreateAsync(request, cancellationToken);

                return Results.Created($"/api/organizations/{created.Id}", created);
            })
            .WithValidation<CreateOrganizationRequest>()
            .WithSummary("Create an organization")
            .WithDescription("The caller becomes its first owner. Available to any authenticated user.")
            .Produces<OrganizationDto>(StatusCodes.Status201Created);

        unscopedGroup.MapGet("/", async (AuthService authService, CancellationToken cancellationToken) =>
            {
                var me = await authService.GetMeAsync(cancellationToken);

                return Results.Ok(me.Organizations);
            })
            .WithSummary("Organizations the caller belongs to")
            .Produces<IReadOnlyList<OrganizationMembershipDto>>();

        var scoped = api
            .MapGroup("/organizations/{organizationId:guid}")
            .RequireAuthorization()
            .AddEndpointFilter<TenantResolutionFilter>();

        scoped.MapGet("/", async (OrganizationService organizations, CancellationToken cancellationToken) =>
                Results.Ok(await organizations.GetCurrentAsync(cancellationToken)))
            .RequirePermission(Permission.OrganizationRead)
            .WithTags("Organizations")
            .WithSummary("Organization details")
            .Produces<OrganizationDto>()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        scoped.MapPatch("/", async (
                UpdateOrganizationRequest request,
                OrganizationService organizations,
                CancellationToken cancellationToken) =>
                Results.Ok(await organizations.UpdateAsync(request, cancellationToken)))
            .WithValidation<UpdateOrganizationRequest>()
            .RequirePermission(Permission.OrganizationUpdate)
            .WithTags("Organizations")
            .WithSummary("Rename the organization")
            .Produces<OrganizationDto>();

        scoped.MapDelete("/", async (
                OrganizationService organizations,
                CancellationToken cancellationToken,
                string? confirm = null) =>
            {
                await organizations.DeleteAsync(confirm, cancellationToken);

                return Results.NoContent();
            })
            .RequirePermission(Permission.OrganizationDelete)
            .WithTags("Organizations")
            .WithSummary("Delete the organization and all its data")
            .WithDescription(
                "Owners only. Requires `?confirm=<slug>` to match the organization's slug, " +
                "because this cascades to members, invitations, projects and tasks. " +
                "The audit trail survives.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        scoped.MapMemberEndpoints();
        scoped.MapInvitationEndpoints();
        scoped.MapProjectEndpoints();
        scoped.MapTaskEndpoints();
        scoped.MapAuditEndpoints();

        return unscopedGroup;
    }
}
