using Microsoft.EntityFrameworkCore;
using Workspace.Domain.Entities;

namespace Workspace.Application.Abstractions;

/// <summary>Data access seam for the application layer.</summary>
public interface IWorkspaceDb
{
    DbSet<User> Users { get; }

    DbSet<Organization> Organizations { get; }

    DbSet<Membership> Memberships { get; }

    DbSet<Invitation> Invitations { get; }

    DbSet<Project> Projects { get; }

    DbSet<TaskItem> Tasks { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    /// <summary>Escape hatch that bypasses the tenant query filter, for the few reads that cannot be tenant-scoped by definition: resolving which organizations a user belongs to, resolving the caller's membership for the requested organization, and redeeming an invitation token before membership exists.</summary>
    IQueryable<TEntity> CrossTenant<TEntity>() where TEntity : class;

    /// <summary>Persists all tracked changes in a single transaction.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
