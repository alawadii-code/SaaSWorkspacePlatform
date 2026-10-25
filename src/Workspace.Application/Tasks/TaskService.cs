using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Workspace.Application.Abstractions;
using Workspace.Application.Common;
using Workspace.Domain;
using Workspace.Domain.Audit;
using Workspace.Domain.Authorization;
using Workspace.Domain.Common;
using Workspace.Domain.Entities;

namespace Workspace.Application.Tasks;

/// <summary>
/// Tasks within the organization resolved for this request.
/// </summary>
/// <remarks>
/// Two authorization layers apply. The permission matrix decides whether the
/// caller may write tasks at all; <see cref="EnsureCanMutate"/> then applies
/// the row-level rule that a Member may only change tasks they created or are
/// assigned to, while Admins and Owners may change any.
/// </remarks>
public sealed class TaskService(
    IWorkspaceDb db,
    ITenantContext tenant,
    IAuditWriter audit,
    IClock clock)
{
    public async Task<PagedResult<TaskDto>> ListAsync(
        PageRequest page,
        Guid? projectId = null,
        TaskState? status = null,
        Guid? assigneeUserId = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.TaskRead);

        var query = db.Tasks.AsQueryable();

        if (projectId is not null)
        {
            query = query.Where(t => t.ProjectId == projectId);
        }

        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }

        if (assigneeUserId is not null)
        {
            query = query.Where(t => t.AssigneeUserId == assigneeUserId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(t => EF.Functions.ILike(t.Title, term));
        }

        return await query
            .OrderBy(t => t.Status)
            .ThenByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAt)
            .Select(Projection)
            .ToPagedResultAsync(page, dto => dto, cancellationToken);
    }

    public async Task<TaskDto> GetAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.TaskRead);

        return await db.Tasks
            .Where(t => t.Id == taskId)
            .Select(Projection)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Task");
    }

    public async Task<TaskDto> CreateAsync(
        Guid projectId,
        CreateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.TaskCreate);

        // Tenant-filtered: a project id belonging to another organization does
        // not resolve here, so a task cannot be created across tenants.
        var project = await db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new NotFoundException("Project");

        if (project.Status == ProjectStatus.Archived)
        {
            throw new ConflictException("Cannot add tasks to an archived project.");
        }

        if (request.AssigneeUserId is not null)
        {
            await EnsureAssigneeIsMemberAsync(request.AssigneeUserId.Value, cancellationToken);
        }

        var now = clock.UtcNow;

        var task = new TaskItem
        {
            OrganizationId = tenant.OrganizationId,
            ProjectId = project.Id,
            Title = request.Title.Trim(),
            Description = Normalize(request.Description),
            Status = TaskState.Todo,
            Priority = request.Priority,
            AssigneeUserId = request.AssigneeUserId,
            CreatedByUserId = tenant.UserId,
            DueAt = request.DueAt,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Tasks.Add(task);

        audit.Record(
            AuditActions.TaskCreated,
            AuditResources.Task,
            task.Id.ToString(),
            new { projectId = project.Id, title = task.Title, assigneeUserId = task.AssigneeUserId });

        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(task.Id, cancellationToken);
    }

    public async Task<TaskDto> UpdateAsync(
        Guid taskId,
        UpdateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new NotFoundException("Task");

        EnsureCanMutate(task);

        var now = clock.UtcNow;
        var previousStatus = task.Status;

        task.Title = request.Title.Trim();
        task.Description = Normalize(request.Description);
        task.Priority = request.Priority;
        task.DueAt = request.DueAt;

        if (task.Status != request.Status)
        {
            task.Status = request.Status;

            // Completion timestamp is derived from the status, never accepted
            // from the client.
            task.CompletedAt = request.Status == TaskState.Done ? now : null;
        }

        task.UpdatedAt = now;

        audit.Record(
            AuditActions.TaskUpdated,
            AuditResources.Task,
            task.Id.ToString(),
            new
            {
                statusFrom = previousStatus.ToString(),
                statusTo = task.Status.ToString(),
                title = task.Title,
            });

        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(task.Id, cancellationToken);
    }

    public async Task<TaskDto> AssignAsync(
        Guid taskId,
        AssignTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.TaskAssign);

        var task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new NotFoundException("Task");

        EnsureCanMutate(task);

        if (request.AssigneeUserId is not null)
        {
            await EnsureAssigneeIsMemberAsync(request.AssigneeUserId.Value, cancellationToken);
        }

        var previous = task.AssigneeUserId;

        task.AssigneeUserId = request.AssigneeUserId;
        task.UpdatedAt = clock.UtcNow;

        audit.Record(
            AuditActions.TaskAssigned,
            AuditResources.Task,
            task.Id.ToString(),
            new { from = previous, to = request.AssigneeUserId });

        await db.SaveChangesAsync(cancellationToken);

        return await GetAsync(task.Id, cancellationToken);
    }

    public async Task DeleteAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.TaskDelete);

        var task = await db.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken)
            ?? throw new NotFoundException("Task");

        audit.Record(
            AuditActions.TaskDeleted,
            AuditResources.Task,
            task.Id.ToString(),
            new { projectId = task.ProjectId, title = task.Title });

        db.Tasks.Remove(task);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Row-level write check: <c>TaskUpdateAny</c> allows editing every task,
    /// <c>TaskUpdateOwn</c> only the caller's own. Phrased as "not found" for
    /// neither, so the rule cannot be used to enumerate tasks.
    /// </summary>
    private void EnsureCanMutate(TaskItem task)
    {
        if (tenant.Has(Permission.TaskUpdateAny))
        {
            return;
        }

        if (tenant.Has(Permission.TaskUpdateOwn) && task.IsOwnedBy(tenant.UserId))
        {
            return;
        }

        throw new ForbiddenException("You can only change tasks you created or that are assigned to you.");
    }

    /// <summary>
    /// An assignee must be a member of the same organization. The membership
    /// set is tenant-filtered, so a user id from another tenant fails here.
    /// </summary>
    private async Task EnsureAssigneeIsMemberAsync(Guid assigneeUserId, CancellationToken cancellationToken)
    {
        var isMember = await db.Memberships
            .AnyAsync(m => m.UserId == assigneeUserId, cancellationToken);

        if (!isMember)
        {
            throw new ValidationFailedException(
                nameof(AssignTaskRequest.AssigneeUserId),
                "The assignee must be a member of this organization.");
        }
    }

    private static string? Normalize(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    /// <summary>
    /// Shared projection, so list and detail responses cannot drift apart.
    /// Held as an expression tree so EF Core translates it to SQL and selects
    /// only these columns instead of loading whole entity graphs.
    /// </summary>
    private static readonly Expression<Func<TaskItem, TaskDto>> Projection =
        t => new TaskDto(
            t.Id,
            t.ProjectId,
            t.Project!.Name,
            t.Title,
            t.Description,
            t.Status,
            t.Priority,
            t.AssigneeUserId,
            t.Assignee != null ? t.Assignee.DisplayName : null,
            t.CreatedByUserId,
            t.DueAt,
            t.CompletedAt,
            t.CreatedAt,
            t.UpdatedAt);
}
