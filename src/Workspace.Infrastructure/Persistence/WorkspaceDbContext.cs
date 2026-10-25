using System.Text;
using Microsoft.EntityFrameworkCore;
using Workspace.Application.Abstractions;
using Workspace.Domain.Common;
using Workspace.Domain.Entities;

namespace Workspace.Infrastructure.Persistence;

/// <summary>
/// The application's single database context.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Tenant isolation.</strong> Every entity that implements
/// <see cref="ITenantOwned"/>, plus <see cref="AuditLog"/>, carries a global
/// query filter pinned to the organization resolved for the current request.
/// A query that forgets its own organization predicate still returns nothing
/// from other tenants, and when no tenant has been resolved the filter matches
/// <see cref="Guid.Empty"/>, which no row ever holds — it fails closed rather
/// than open.
/// </para>
/// <para>
/// <strong>Write-side guard.</strong> Query filters do not apply to inserts,
/// so <see cref="SaveChangesAsync"/> additionally refuses to insert a
/// tenant-owned row whose organization is not the resolved one. That turns a
/// coding mistake into an immediate exception instead of a cross-tenant write.
/// </para>
/// </remarks>
public sealed class WorkspaceDbContext(
    DbContextOptions<WorkspaceDbContext> options,
    ITenantContext tenant)
    : DbContext(options), IWorkspaceDb
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Organization the filters are pinned to. <see cref="Guid.Empty"/> when no
    /// membership has been resolved, which matches no row.
    /// </summary>
    private Guid CurrentOrganizationId => tenant.IsResolved ? tenant.OrganizationId : Guid.Empty;

    /// <inheritdoc />
    public IQueryable<TEntity> CrossTenant<TEntity>() where TEntity : class =>
        Set<TEntity>().IgnoreQueryFilters();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkspaceDbContext).Assembly);

        // Tenant filters. Declared here rather than in each configuration so
        // the whole isolation story is readable in one place.
        modelBuilder.Entity<Membership>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<Invitation>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<Project>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<TaskItem>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);

        // Account-level audit rows have no organization, so they never match a
        // tenant-scoped read: only operators see them, in the server log.
        modelBuilder.Entity<AuditLog>().HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);

        ApplySnakeCaseNames(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAt == default)
                    {
                        entry.Entity.CreatedAt = now;
                    }

                    if (entry.Entity.UpdatedAt == default)
                    {
                        entry.Entity.UpdatedAt = entry.Entity.CreatedAt;
                    }

                    GuardTenantOnInsert(entry.Entity);
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;

                default:
                    break;
            }
        }

        GuardAuditImmutability();

        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Refuses to insert a tenant-owned row into an organization other than
    /// the resolved one. Skipped when no tenant is resolved, which is the case
    /// for the flows that legitimately write across tenants: creating an
    /// organization, and accepting an invitation.
    /// </summary>
    private void GuardTenantOnInsert(Entity entity)
    {
        if (!tenant.IsResolved || entity is not ITenantOwned owned)
        {
            return;
        }

        if (owned.OrganizationId != tenant.OrganizationId)
        {
            throw new InvalidOperationException(
                $"Refusing to write {entity.GetType().Name} into organization {owned.OrganizationId} " +
                $"while organization {tenant.OrganizationId} is resolved.");
        }
    }

    /// <summary>
    /// Application-level half of the audit guarantee: audit rows may be
    /// inserted, never changed or removed. The database enforces the same rule
    /// with a trigger, so bypassing EF does not help either.
    /// </summary>
    private void GuardAuditImmutability()
    {
        var tampered = ChangeTracker.Entries<AuditLog>()
            .Any(entry => entry.State is EntityState.Modified or EntityState.Deleted);

        if (tampered)
        {
            throw new InvalidOperationException("Audit log entries are append-only.");
        }
    }

    /// <summary>
    /// Maps CLR names to snake_case database names, so the schema reads
    /// naturally in psql and in migration SQL. Done as a convention rather
    /// than with a package so there is one less dependency to pin.
    /// </summary>
    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();

            if (tableName is not null)
            {
                entity.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                var name = key.GetName();

                if (name is not null)
                {
                    key.SetName(ToSnakeCase(name));
                }
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                var name = foreignKey.GetConstraintName();

                if (name is not null)
                {
                    foreignKey.SetConstraintName(ToSnakeCase(name));
                }
            }

            foreach (var index in entity.GetIndexes())
            {
                var name = index.GetDatabaseName();

                if (name is not null)
                {
                    index.SetDatabaseName(ToSnakeCase(name));
                }
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (char.IsUpper(current))
            {
                var previous = i > 0 ? name[i - 1] : '\0';
                var isBoundary = i > 0 && previous != '_' && (!char.IsUpper(previous) || (i + 1 < name.Length && char.IsLower(name[i + 1])));

                if (isBoundary)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
