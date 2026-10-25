using Microsoft.EntityFrameworkCore;
using Workspace.Domain.Entities;

namespace Workspace.Application.Abstractions;

/// <summary>
/// Data access seam for the application layer.
/// </summary>
/// <remarks>
/// <para>
/// This is EF Core's <c>DbSet</c> surface rather than a set of hand-rolled
/// repositories. The trade-off is deliberate: repositories over an ORM that is
/// already a repository tend to leak (<c>IQueryable</c> escapes, or the
/// interface grows one method per query), while this seam keeps services
/// testable and lets each use case express exactly the query it needs.
/// </para>
/// <para>
/// Every tenant-owned entity carries a global query filter, so reads through
/// these sets are automatically scoped to the resolved organization. The three
/// operations that must legitimately cross tenants go through
/// <see cref="CrossTenant{TEntity}"/> so they can be found and reviewed by name.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Escape hatch that bypasses the tenant query filter, for the few reads
    /// that cannot be tenant-scoped by definition: resolving which
    /// organizations a user belongs to, resolving the caller's membership for
    /// the requested organization, and redeeming an invitation token before
    /// membership exists.
    /// </summary>
    /// <remarks>
    /// Callers must still constrain the query by a value that belongs to the
    /// caller (their user id, or a hashed single-use token).
    /// </remarks>
    IQueryable<TEntity> CrossTenant<TEntity>() where TEntity : class;

    /// <summary>
    /// Persists all tracked changes in a single transaction. Commands write
    /// their entity changes and the matching audit entry before calling this
    /// once, so an action and its audit record are atomic.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
