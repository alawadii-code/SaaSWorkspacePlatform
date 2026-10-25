using Workspace.Domain.Entities;

namespace Workspace.Application.Abstractions;

/// <summary>Password hashing and verification.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Verifies a password, reporting whether the stored hash used outdated
    /// parameters and should be rewritten on successful login.
    /// </summary>
    PasswordVerificationResult Verify(string hash, string password);
}

public enum PasswordVerificationResult
{
    Failed = 0,
    Success = 1,
    SuccessRehashNeeded = 2,
}

/// <summary>Issues access tokens and the opaque refresh tokens that renew them.</summary>
public interface ITokenService
{
    /// <summary>
    /// Creates a short-lived signed access token. Claims carry identity only —
    /// never roles or permissions, which are resolved per request from the
    /// membership table so a revoked role takes effect immediately.
    /// </summary>
    AccessToken CreateAccessToken(User user);

    /// <summary>Generates a new refresh token: plaintext for the client, hash for storage.</summary>
    (string Plaintext, string Hash) CreateRefreshToken();

    /// <summary>
    /// Generates a single-use, URL-safe token with 256 bits of entropy, used
    /// for invitation links. Only the hash is ever persisted.
    /// </summary>
    (string Plaintext, string Hash) CreateOpaqueToken();

    /// <summary>Hashes a refresh or invitation token for constant-time lookup by hash.</summary>
    string HashToken(string plaintext);

    TimeSpan RefreshTokenLifetime { get; }
}

/// <summary>A signed access token and its expiry.</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt, int ExpiresInSeconds);

/// <summary>Abstracts the clock so time-dependent rules are testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Default clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
