using Workspace.Domain.Common;

namespace Workspace.Domain.Entities;

/// <summary>A long-lived credential that mints new access tokens.</summary>
public class RefreshToken : Entity
{
    public Guid UserId { get; set; }

    /// <summary>Base64 SHA-256 hash of the opaque token.</summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Why the token was revoked: <c>rotated</c>, <c>logout</c>, <c>reuse_detected</c>.</summary>
    public string? RevokedReason { get; set; }

    /// <summary>Token that replaced this one, forming the rotation chain.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>First token in the rotation chain.</summary>
    public Guid ChainId { get; set; }

    public string? CreatedByIp { get; set; }

    public string? UserAgent { get; set; }

    public User? User { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
