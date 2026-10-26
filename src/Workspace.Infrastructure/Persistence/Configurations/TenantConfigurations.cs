using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workspace.Domain;
using Workspace.Domain.Entities;

namespace Workspace.Infrastructure.Persistence.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).HasMaxLength(100).IsRequired();
        builder.Property(o => o.Slug).HasMaxLength(48).IsRequired();

        builder.HasIndex(o => o.Slug).IsUnique();
    }
}

public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");

        builder.HasKey(m => m.Id);

        // One membership per user per organization.
        builder.HasIndex(m => new { m.OrganizationId, m.UserId }).IsUnique();

        builder.HasIndex(m => m.UserId);

        builder.HasIndex(m => new { m.OrganizationId, m.Role });

        builder.HasOne(m => m.Organization)
            .WithMany(o => o.Memberships)
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.User)
            .WithMany(u => u.Memberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Email).HasMaxLength(254).IsRequired();
        builder.Property(i => i.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(i => i.DeliveryError).HasMaxLength(512);

        // Redeeming an invitation is a lookup by token hash.
        builder.HasIndex(i => i.TokenHash).IsUnique();

        builder.HasIndex(i => new { i.OrganizationId, i.Status });

        builder.HasIndex(i => new { i.OrganizationId, i.Email })
            .IsUnique()
            .HasDatabaseName("ix_invitations_pending_per_email")
            .HasFilter($"status = {(int)InvitationStatus.Pending}");

        builder.HasOne(i => i.Organization)
            .WithMany(o => o.Invitations)
            .HasForeignKey(i => i.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.InvitedByUser)
            .WithMany()
            .HasForeignKey(i => i.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
