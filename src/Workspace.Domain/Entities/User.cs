using Workspace.Domain.Common;

namespace Workspace.Domain.Entities;

/// <summary>A person's global identity.</summary>
public class User : Entity
{
    /// <summary>Address as the user typed it, for display and outbound mail.</summary>
    public required string Email { get; set; }

    /// <summary>Lower-cased email used for lookups and the uniqueness constraint, so <c>A@x.com</c> cannot register a second account alongside <c>a@x.com</c>.</summary>
    public required string NormalizedEmail { get; set; }

    /// <summary>PBKDF2-HMAC-SHA512 hash (ASP.NET Core Identity v3 format).</summary>
    public required string PasswordHash { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>Disabled accounts keep their data but cannot authenticate.</summary>
    public bool IsDisabled { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Consecutive failed sign-in attempts; reset on success.</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>Set after too many consecutive failures, to blunt online password guessing.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    public ICollection<Membership> Memberships { get; set; } = [];

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is not null && LockedUntil > now;
}
