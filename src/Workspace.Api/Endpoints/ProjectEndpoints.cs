using Microsoft.AspNetCore.Mvc;
using Workspace.Api.Http;
using Workspace.Application.Common;
using Workspace.Application.Projects;
using Workspace.Domain;
using Workspace.Domain.Authorization;

namespace Workspace.Api.Endpoints;

public static class ProjectEndpoints
{
    public static RouteGroupBuilder MapProjectEndpoints(this RouteGroupBuilder scoped)
    {
        var group = scoped.MapGroup("/projects").WithTags("Projects");

        group.MapGet("/", async (
                ProjectService projects,
                CancellationToken cancellationToken,
                string? search = null,
                ProjectStatus? status = null,
                int? page = null,
                int? pageSize = null) =>
                Results.Ok(await projects.ListAsync(new PageRequest(page, pageSize), search, status, cancellationToken)))
            .RequirePermission(Permission.ProjectRead)
            .WithSummary("List projects")
            .Produces<PagedResult<ProjectDto>>();

        group.MapGet("/{projectId:guid}", async (
                Guid projectId,
                ProjectService projects,
                CancellationToken cancellationToken) =>
                Results.Ok(await projects.GetAsync(projectId, cancellationToken)))
            .RequirePermission(Permission.ProjectRead)
            .WithSummary("Get a project")
            .WithDescription("A project belonging to another organization is reported as not found.")
            .Produces<ProjectDto>()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
                CreateProjectRequest request,
                ProjectService projects,
                CancellationToken cancellationToken) =>
            {
                var project = await projects.CreateAsync(request, cancellationToken);

                return Results.Created($"/api/projects/{project.Id}", project);
            })
            .WithValidation<CreateProjectRequest>()
            .RequirePermission(Permission.ProjectCreate)
            .WithSummary("Create a project")
            .Produces<ProjectDto>(StatusCodes.Status201Created);

        group.MapPut("/{projectId:guid}", async (
                Guid projectId,
                UpdateProjectRequest request,
                ProjectService projects,
                CancellationToken cancellationToken) =>
                Results.Ok(await projects.UpdateAsync(projectId, request, cancellationToken)))
            .WithValidation<UpdateProjectRequest>()
            .RequirePermission(Permission.ProjectUpdate)
            .WithSummary("Update or archive a project")
            .Produces<ProjectDto>();

        group.MapDelete("/{projectId:guid}", async (
                Guid projectId,
                ProjectService projects,
                CancellationToken cancellationToken) =>
            {
                await projects.DeleteAsync(projectId, cancellationToken);

                return Results.NoContent();
            })
            .RequirePermission(Permission.ProjectDelete)
            .WithSummary("Delete a project and its tasks")
            .Produces(StatusCodes.Status204NoContent);

        return group;
    }
}
