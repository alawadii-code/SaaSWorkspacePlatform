using Workspace.Domain.Authorization;

namespace Workspace.Application.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string? RefreshToken);

/// <summary>
/// Tokens returned after authentication. The refresh token is also set as an
/// HttpOnly cookie by the API; it is included in the body so non-browser
/// clients can use the API too.
/// </summary>
public sealed record AuthResult(
    string AccessToken,
    string TokenType,
    int ExpiresInSeconds,
    string RefreshToken,
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
