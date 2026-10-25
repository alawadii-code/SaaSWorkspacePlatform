using Microsoft.EntityFrameworkCore;
using Workspace.Application.Abstractions;
using Workspace.Domain.Audit;
using Workspace.Domain.Authorization;
using Workspace.Domain.Common;
using Workspace.Domain.Entities;

namespace Workspace.Application.Auth;

/// <summary>
/// Registration, sign-in, refresh-token rotation and sign-out.
/// </summary>
/// <remarks>
/// Design notes that matter for security review:
/// <list type="bullet">
/// <item>
/// Access tokens carry identity only. Roles and permissions are resolved from
/// the membership table on every request, so revoking a role takes effect
/// immediately instead of when the token expires.
/// </item>
/// <item>
/// Every sign-in failure returns one generic message, whether the account does
/// not exist, the password is wrong, the account is disabled or it is
/// temporarily locked. A wrong email also pays the cost of a hash
/// verification, so response time does not reveal which accounts exist.
/// </item>
/// <item>
/// Refresh tokens are opaque random values stored only as SHA-256 hashes, and
/// they rotate on every use. Presenting an already-rotated token is treated as
/// theft: the whole rotation chain is revoked and the event is audited.
/// </item>
/// </list>
/// </remarks>
public sealed class AuthService(
    IWorkspaceDb db,
    IPasswordHasher passwordHasher,
    ITokenService tokens,
    IAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock)
{
    /// <summary>Consecutive failures before an account is temporarily locked.</summary>
    public const int MaxFailedAttempts = 10;

    /// <summary>How long a lockout lasts.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private const string GenericAuthFailure = "Invalid email or password.";

    /// <summary>
    /// A valid hash verified when no account matches, so that a request for an
    /// unknown address costs the same as one for a known address.
    /// </summary>
    private static string? decoyHash;

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = User.Normalize(request.Email);

        // Deliberate trade-off: registration reports a duplicate address so the
        // user can go and sign in instead. It therefore discloses that an
        // account exists. Sign-in and (in a fuller product) password reset do
        // not disclose it, which is where enumeration actually pays off for an
        // attacker.
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            throw new ConflictException("An account with that email already exists.");
        }

        var now = clock.UtcNow;

        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Users.Add(user);

        var refresh = IssueRefreshToken(user, now);

        audit.RecordFor(
            organizationId: null,
            actorUserId: user.Id,
            actorEmail: user.Email,
            AuditActions.UserRegistered,
            AuditResources.User,
            user.Id.ToString());

        await db.SaveChangesAsync(cancellationToken);

        return BuildResult(user, refresh.Plaintext);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = User.Normalize(request.Email);
        var now = clock.UtcNow;

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is null)
        {
            // Equalize work with the found-account path before failing.
            passwordHasher.Verify(GetDecoyHash(), request.Password);

            audit.RecordFor(
                organizationId: null,
                actorUserId: null,
                actorEmail: normalizedEmail,
                AuditActions.UserLoginFailed,
                AuditResources.User,
                metadata: new { reason = "unknown_account" });

            await db.SaveChangesAsync(cancellationToken);

            throw new UnauthorizedException(GenericAuthFailure);
        }

        var verification = passwordHasher.Verify(user.PasswordHash, request.Password);
        var lockedOut = user.IsLockedOut(now);

        if (verification == PasswordVerificationResult.Failed || lockedOut || user.IsDisabled)
        {
            // Only count a genuinely wrong password toward the lockout, so a
            // lockout cannot be extended indefinitely by replaying requests
            // against an already-locked account.
            if (!lockedOut && verification == PasswordVerificationResult.Failed)
            {
                user.FailedLoginAttempts++;

                if (user.FailedLoginAttempts >= MaxFailedAttempts)
                {
                    user.LockedUntil = now.Add(LockoutDuration);
                    user.FailedLoginAttempts = 0;
                }

                user.UpdatedAt = now;
            }

            audit.RecordFor(
                organizationId: null,
                actorUserId: user.Id,
                actorEmail: user.Email,
                AuditActions.UserLoginFailed,
                AuditResources.User,
                user.Id.ToString(),
                new
                {
                    reason = user.IsDisabled ? "account_disabled"
                        : lockedOut ? "account_locked"
                        : "bad_password",
                });

            await db.SaveChangesAsync(cancellationToken);

            throw new UnauthorizedException(GenericAuthFailure);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // Stored hash used weaker parameters than the current defaults;
            // upgrade it now that the plaintext is available.
            user.PasswordHash = passwordHasher.Hash(request.Password);
        }

        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        user.UpdatedAt = now;

        var refresh = IssueRefreshToken(user, now);

        audit.RecordFor(
            organizationId: null,
            actorUserId: user.Id,
            actorEmail: user.Email,
            AuditActions.UserLoggedIn,
            AuditResources.User,
            user.Id.ToString());

        await db.SaveChangesAsync(cancellationToken);

        return BuildResult(user, refresh.Plaintext);
    }

    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new UnauthorizedException("A refresh token is required.");
        }

        var hash = tokens.HashToken(refreshToken);
        var now = clock.UtcNow;

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            throw new UnauthorizedException("Invalid refresh token.");
        }

        if (stored.RevokedAt is not null)
        {
            await RevokeChainAsync(stored.ChainId, "reuse_detected", now, cancellationToken);

            audit.RecordFor(
                organizationId: null,
                actorUserId: stored.UserId,
                actorEmail: stored.User?.Email,
                AuditActions.TokenRefreshReuseDetected,
                AuditResources.Session,
                stored.ChainId.ToString(),
                new { revokedReason = stored.RevokedReason });

            await db.SaveChangesAsync(cancellationToken);

            throw new UnauthorizedException("This session has been revoked. Please sign in again.");
        }

        if (stored.ExpiresAt <= now)
        {
            throw new UnauthorizedException("Refresh token has expired.");
        }

        var user = stored.User;

        if (user is null || user.IsDisabled)
        {
            throw new UnauthorizedException("Invalid refresh token.");
        }

        var replacement = IssueRefreshToken(user, now, stored.ChainId);

        stored.RevokedAt = now;
        stored.RevokedReason = "rotated";
        stored.ReplacedByTokenId = replacement.Token.Id;
        stored.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return BuildResult(user, replacement.Plaintext);
    }

    /// <summary>
    /// Revokes the presented token and everything else in its rotation chain,
    /// so signing out ends the session rather than one token in it.
    /// </summary>
    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = tokens.HashToken(refreshToken);
        var now = clock.UtcNow;

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            // Nothing to revoke. Report success either way: a caller cannot
            // learn whether a token was valid by signing out with it.
            return;
        }

        await RevokeChainAsync(stored.ChainId, "logout", now, cancellationToken);

        audit.RecordFor(
            organizationId: null,
            actorUserId: stored.UserId,
            actorEmail: stored.User?.Email,
            AuditActions.UserLoggedOut,
            AuditResources.Session,
            stored.ChainId.ToString());

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The caller's profile and the organizations they belong to, each with the
    /// permissions their role grants there.
    /// </summary>
    public async Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new UnauthorizedException();

        // Cross-tenant by definition: this answers "which tenants am I in?".
        // Constrained to the caller's own user id.
        var memberships = await db.CrossTenant<Membership>()
            .Where(m => m.UserId == userId)
            .Include(m => m.Organization)
            .OrderBy(m => m.Organization!.Name)
            .ToListAsync(cancellationToken);

        var organizations = memberships
            .Where(m => m.Organization is not null)
            .Select(m => new OrganizationMembershipDto(
                m.OrganizationId,
                m.Organization!.Name,
                m.Organization.Slug,
                m.Role,
                RolePermissions.NamesFor(m.Role),
                m.JoinedAt))
            .ToList();

        return new MeResponse(ToDto(user), organizations);
    }

    private async Task RevokeChainAsync(Guid chainId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var chain = await db.RefreshTokens
            .Where(t => t.ChainId == chainId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in chain)
        {
            token.RevokedAt = now;
            token.RevokedReason = reason;
            token.UpdatedAt = now;
        }
    }

    private (RefreshToken Token, string Plaintext) IssueRefreshToken(User user, DateTimeOffset now, Guid? chainId = null)
    {
        var (plaintext, hash) = tokens.CreateRefreshToken();

        var token = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = now.Add(tokens.RefreshTokenLifetime),
            CreatedByIp = currentUser.IpAddress,
            UserAgent = currentUser.UserAgent,
            CreatedAt = now,
            UpdatedAt = now,
        };

        token.ChainId = chainId ?? token.Id;

        db.RefreshTokens.Add(token);

        return (token, plaintext);
    }

    private AuthResult BuildResult(User user, string refreshToken)
    {
        var accessToken = tokens.CreateAccessToken(user);

        return new AuthResult(
            accessToken.Value,
            "Bearer",
            accessToken.ExpiresInSeconds,
            refreshToken,
            ToDto(user));
    }

    private static UserDto ToDto(User user) =>
        new(user.Id, user.Email, user.DisplayName, user.CreatedAt);

    private string GetDecoyHash() =>
        decoyHash ??= passwordHasher.Hash($"decoy-{Guid.NewGuid():N}");
}
