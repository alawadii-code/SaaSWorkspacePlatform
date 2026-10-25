using Workspace.Domain.Authorization;

namespace Workspace.Application.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string? RefreshToken);

/// <summary>
/// Tokens returned after authentication.
/// </summary>
/// <remarks>
/// The API always sets the refresh token as an HttpOnly, SameSite cookie,
/// which keeps it out of reach of any script on the page. It appears in
/// <see cref="RefreshToken"/> only when <c>Auth:ReturnRefreshTokenInBody</c>
/// is enabled, for non-browser clients that cannot hold a cookie jar; the
/// default is off, because a token readable from JavaScript is a token an XSS
/// bug can steal.
/// </remarks>
public sealed record AuthResult(
    string AccessToken,
    string TokenType,
    int ExpiresInSeconds,
    string? RefreshToken,
    UserDto User);

public sealed record UserDto(Guid Id, string Email, string DisplayName, DateTimeOffset CreatedAt);

/// <summary>One organization the caller belongs to, with their effective permissions in it.</summary>
public sealed record OrganizationMembershipDto(
    Guid OrganizationId,
    string Name,
    string Slug,
    OrganizationRole Role,
    IReadOnlyList<string> Permissions,
    DateTimeOffset JoinedAt);

/// <summary>
/// Identity plus tenant list for the signed-in caller. The permission list is
/// sent so the frontend can hide controls the caller cannot use; the server
/// re-checks every request regardless.
/// </summary>
public sealed record MeResponse(UserDto User, IReadOnlyList<OrganizationMembershipDto> Organizations);
