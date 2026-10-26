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

        builder.HasIndex(p => new { p.OrganizationId, p.Slug }).IsUnique();
        builder.HasIndex(p => new { p.OrganizationId, p.Status });

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

        builder.HasOne(t => t.Project)
            .WithMany(p => p.Tasks)
            .HasForeignKey(t => new { t.OrganizationId, t.ProjectId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id })
            .OnDelete(DeleteBehavior.Cascade);

        // sOoo a deleted user leaves their tasks in place, unassigned.
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

        builder.Property(a => a.Metadata).HasColumnType("jsonb");

        // ok sOoo the audit endpoint reads one organization's trail newest-first ngl
        builder.HasIndex(a => new { a.OrganizationId, a.CreatedAt });
        builder.HasIndex(a => new { a.OrganizationId, a.Action });
        builder.HasIndex(a => a.ActorUserId);

    }
}
