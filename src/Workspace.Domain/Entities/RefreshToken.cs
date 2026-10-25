using Workspace.Domain.Common;

namespace Workspace.Domain.Entities;

/// <summary>
/// A long-lived credential that mints new access tokens.
/// </summary>
/// <remarks>
/// Stored as a SHA-256 hash of 256 bits of cryptographically random data, so
/// the table cannot be used to impersonate anyone if it leaks. Tokens rotate on
/// every use: the old row is revoked and points at its replacement. Presenting
/// an already-rotated token means it was captured, so the whole chain is
/// revoked and the session ends.
/// </remarks>
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

    /// <summary>
    /// First token in the rotation chain. Lets the whole session be revoked in
    /// one statement when a replayed token is detected.
    /// </summary>
    public Guid ChainId { get; set; }

    public string? CreatedByIp { get; set; }

    public string? UserAgent { get; set; }

    public User? User { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
