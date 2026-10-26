using Microsoft.AspNetCore.Mvc;
using Workspace.Api.Http;
using Workspace.Application.Common;
using Workspace.Application.Tasks;
using Workspace.Domain;
using Workspace.Domain.Authorization;

namespace Workspace.Api.Endpoints;

public static class TaskEndpoints
{
    public static RouteGroupBuilder MapTaskEndpoints(this RouteGroupBuilder scoped)
    {
        var group = scoped.MapGroup("/tasks").WithTags("Tasks");

        group.MapGet("/", async (
                TaskService tasks,
                CancellationToken cancellationToken,
                Guid? projectId = null,
                TaskState? status = null,
                Guid? assigneeUserId = null,
                string? search = null,
                int? page = null,
                int? pageSize = null) =>
                Results.Ok(await tasks.ListAsync(
                    new PageRequest(page, pageSize),
                    projectId,
                    status,
                    assigneeUserId,
                    search,
                    cancellationToken)))
            .RequirePermission(Permission.TaskRead)
            .WithSummary("List tasks")
            .Produces<PagedResult<TaskDto>>();

        group.MapGet("/{taskId:guid}", async (
                Guid taskId,
                TaskService tasks,
                CancellationToken cancellationToken) =>
                Results.Ok(await tasks.GetAsync(taskId, cancellationToken)))
            .RequirePermission(Permission.TaskRead)
            .WithSummary("Get a task")
            .Produces<TaskDto>()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPut("/{taskId:guid}", async (
                Guid taskId,
                UpdateTaskRequest request,
                TaskService tasks,
                CancellationToken cancellationToken) =>
                Results.Ok(await tasks.UpdateAsync(taskId, request, cancellationToken)))
            .WithValidation<UpdateTaskRequest>()
            .WithSummary("Update a task")
            .WithDescription(
                "Admins and owners may update any task. Members may update only tasks they " +
                "created or are assigned to. Viewers may not update tasks.")
            .Produces<TaskDto>()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        group.MapPost("/{taskId:guid}/assign", async (
                Guid taskId,
                AssignTaskRequest request,
                TaskService tasks,
                CancellationToken cancellationToken) =>
                Results.Ok(await tasks.AssignAsync(taskId, request, cancellationToken)))
            .RequirePermission(Permission.TaskAssign)
            .WithSummary("Assign or unassign a task")
            .WithDescription("The assignee must be a member of this organization; send null to clear it.")
            .Produces<TaskDto>()
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{taskId:guid}", async (
                Guid taskId,
                TaskService tasks,
                CancellationToken cancellationToken) =>
            {
                await tasks.DeleteAsync(taskId, cancellationToken);

                return Results.NoContent();
            })
            .RequirePermission(Permission.TaskDelete)
            .WithSummary("Delete a task")
            .Produces(StatusCodes.Status204NoContent);

        scoped.MapPost("/projects/{projectId:guid}/tasks", async (
                Guid projectId,
                CreateTaskRequest request,
                TaskService tasks,
                CancellationToken cancellationToken) =>
            {
                var task = await tasks.CreateAsync(projectId, request, cancellationToken);

                return Results.Created($"/api/tasks/{task.Id}", task);
            })
            .WithValidation<CreateTaskRequest>()
            .RequirePermission(Permission.TaskCreate)
            .WithTags("Tasks")
            .WithSummary("Create a task in a project")
            .Produces<TaskDto>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return group;
    }
}
