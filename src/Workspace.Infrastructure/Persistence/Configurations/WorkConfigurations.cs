using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workspace.Domain.Entities;

namespace Workspace.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(48).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000);

        // Slugs are unique per tenant, not globally: two organizations may
        // each have a "website-redesign".
        builder.HasIndex(p => new { p.OrganizationId, p.Slug }).IsUnique();
        builder.HasIndex(p => new { p.OrganizationId, p.Status });

        // Alternate key that lets tasks reference (organization, project) as a
        // unit, so a task cannot point at another tenant's project.
        builder.HasAlternateKey(p => new { p.OrganizationId, p.Id });

        builder.HasOne(p => p.Organization)
            .WithMany(o => o.Projects)
            .HasForeignKey(p => p.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("tasks");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(5000);

        builder.HasIndex(t => new { t.OrganizationId, t.ProjectId, t.Status });
        builder.HasIndex(t => new { t.OrganizationId, t.AssigneeUserId });

        // Composite foreign key into the project's (organization, id)
        // alternate key. The database therefore rejects a task whose
        // organization does not match its project's, which makes cross-tenant
        // re-parenting impossible rather than merely prevented in code.
        builder.HasOne(t => t.Project)
            .WithMany(p => p.Tasks)
            .HasForeignKey(t => new { t.OrganizationId, t.ProjectId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // A deleted user leaves their tasks in place, unassigned.
        builder.HasOne(t => t.Assignee)
            .WithMany()
            .HasForeignKey(t => t.AssigneeUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).HasMaxLength(64).IsRequired();
        builder.Property(a => a.ResourceType).HasMaxLength(32).IsRequired();
        builder.Property(a => a.ResourceId).HasMaxLength(64);
        builder.Property(a => a.ActorEmail).HasMaxLength(254);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.UserAgent).HasMaxLength(256);
        builder.Property(a => a.CorrelationId).HasMaxLength(64);

        // jsonb rather than text: queryable, and it validates the structure on
        // the way in.
        builder.Property(a => a.Metadata).HasColumnType("jsonb");

        // The audit endpoint reads one organization's trail newest-first.
        builder.HasIndex(a => new { a.OrganizationId, a.CreatedAt });
        builder.HasIndex(a => new { a.OrganizationId, a.Action });
        builder.HasIndex(a => a.ActorUserId);

        // No foreign keys on purpose. Audit entries are historical facts: they
        // must survive deletion of the organization, user or resource they
        // describe, and an ON DELETE action would otherwise have to mutate
        // rows that the immutability trigger forbids changing.
    }
}
