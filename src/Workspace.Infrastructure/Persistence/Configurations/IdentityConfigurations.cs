using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workspace.Domain.Entities;

namespace Workspace.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).HasMaxLength(254).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(80).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();

        builder.HasIndex(u => u.NormalizedEmail).IsUnique();
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(t => t.RevokedReason).HasMaxLength(64);
        builder.Property(t => t.CreatedByIp).HasMaxLength(64);
        builder.Property(t => t.UserAgent).HasMaxLength(256);

        // Lookups are by hash; the token itself is never stored.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Revoking a whole session means revoking a rotation chain.
        builder.HasIndex(t => t.ChainId);

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
