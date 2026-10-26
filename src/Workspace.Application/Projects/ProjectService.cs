using Microsoft.EntityFrameworkCore;
using Workspace.Application.Abstractions;
using Workspace.Application.Common;
using Workspace.Domain;
using Workspace.Domain.Audit;
using Workspace.Domain.Authorization;
using Workspace.Domain.Common;
using Workspace.Domain.Entities;

namespace Workspace.Application.Projects;

/// <summary>Projects within the organization resolved for this request.</summary>
public sealed class ProjectService(
    IWorkspaceDb db,
    ITenantContext tenant,
    IAuditWriter audit,
    IClock clock)
{
    public async Task<PagedResult<ProjectDto>> ListAsync(
        PageRequest page,
        string? search = null,
        ProjectStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.ProjectRead);

        var query = db.Projects.AsQueryable();

        if (status is not null)
        {
            query = query.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }

        return await query
            .OrderBy(p => p.Status)
            .ThenByDescending(p => p.CreatedAt)
            .Select(p => new ProjectDto(
                p.Id,
                p.Name,
                p.Slug,
                p.Description,
                p.Status,
                p.Tasks.Count,
                p.Tasks.Count(t => t.Status != TaskState.Done),
                p.CreatedByUserId,
                p.CreatedAt,
                p.ArchivedAt))
            .ToPagedResultAsync(page, dto => dto, cancellationToken);
    }

    public async Task<ProjectDto> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.ProjectRead);

        return await db.Projects
            .Where(p => p.Id == projectId)
            .Select(p => new ProjectDto(
                p.Id,
                p.Name,
                p.Slug,
                p.Description,
                p.Status,
                p.Tasks.Count,
                p.Tasks.Count(t => t.Status != TaskState.Done),
                p.CreatedByUserId,
                p.CreatedAt,
                p.ArchivedAt))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Project");
    }

    public async Task<ProjectDto> CreateAsync(
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.ProjectCreate);

        var now = clock.UtcNow;

        var project = new Project
        {
            OrganizationId = tenant.OrganizationId,
            Name = request.Name.Trim(),
            Slug = await GenerateUniqueSlugAsync(request.Name, cancellationToken),
            Description = Normalize(request.Description),
            Status = ProjectStatus.Active,
            CreatedByUserId = tenant.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Projects.Add(project);

        audit.Record(
            AuditActions.ProjectCreated,
            AuditResources.Project,
            project.Id.ToString(),
            new { name = project.Name, slug = project.Slug });

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(project, taskCount: 0, openTaskCount: 0);
    }

    public async Task<ProjectDto> UpdateAsync(
        Guid projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.ProjectUpdate);

        var project = await db.Projects
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new NotFoundException("Project");

        var now = clock.UtcNow;
        var changes = new Dictionary<string, object?>();

        if (!string.Equals(project.Name, request.Name.Trim(), StringComparison.Ordinal))
        {
            changes["name"] = new { from = project.Name, to = request.Name.Trim() };
            project.Name = request.Name.Trim();
        }

        var description = Normalize(request.Description);

        if (!string.Equals(project.Description, description, StringComparison.Ordinal))
        {
            changes["description"] = "changed";
            project.Description = description;
        }

        if (project.Status != request.Status)
        {
            changes["status"] = new { from = project.Status.ToString(), to = request.Status.ToString() };
            project.Status = request.Status;
            project.ArchivedAt = request.Status == ProjectStatus.Archived ? now : null;
        }

        project.UpdatedAt = now;

        audit.Record(
            AuditActions.ProjectUpdated,
            AuditResources.Project,
            project.Id.ToString(),
            new { changes });

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(
            project,
            project.Tasks.Count,
            project.Tasks.Count(t => t.Status != TaskState.Done));
    }

    public async Task DeleteAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        tenant.Require(Permission.ProjectDelete);

        var project = await db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new NotFoundException("Project");

        audit.Record(
            AuditActions.ProjectDeleted,
            AuditResources.Project,
            project.Id.ToString(),
            new { name = project.Name, slug = project.Slug });

        // Tasks cascade at the database level tbh
        db.Projects.Remove(project);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Normalize(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private async Task<string> GenerateUniqueSlugAsync(string name, CancellationToken cancellationToken)
    {
        var candidate = Slug.From(name);

        if (!await db.Projects.AnyAsync(p => p.Slug == candidate, cancellationToken))
        {
            return candidate;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var suffixed = Slug.WithSuffix(candidate, Guid.NewGuid().ToString("N")[..6]);

            if (!await db.Projects.AnyAsync(p => p.Slug == suffixed, cancellationToken))
            {
                return suffixed;
            }
        }

        throw new ConflictException("Could not allocate a unique project handle. Try a different name.");
    }

    private static ProjectDto ToDto(Project project, int taskCount, int openTaskCount) =>
        new(
            project.Id,
            project.Name,
            project.Slug,
            project.Description,
            project.Status,
            taskCount,
            openTaskCount,
            project.CreatedByUserId,
            project.CreatedAt,
            project.ArchivedAt);
}
