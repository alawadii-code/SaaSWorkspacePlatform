using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Workspace.Api.Http;
using Workspace.Application.Abstractions;
using Workspace.Application.Auth;

namespace Workspace.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api
            .MapGroup("/auth")
            .WithTags("Authentication")
            .RequireRateLimiting(RateLimitingOptions.AuthPolicy);

        group.MapPost("/register", async (
                RegisterRequest request,
                AuthService authService,
                HttpContext httpContext,
                IOptions<AuthOptions> authOptions,
                ITokenService tokens,
                CancellationToken cancellationToken) =>
            {
                var result = await authService.RegisterAsync(request, cancellationToken);

                return AuthResponse.Write(httpContext, result, authOptions.Value, tokens.RefreshTokenLifetime);
            })
            .WithValidation<RegisterRequest>()
            .WithSummary("Create an account")
            .WithDescription(
                "Creates a user and signs them in. Does not create an organization: " +
                "the caller either creates one or accepts an invitation.")
            .Produces<AuthResult>()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapPost("/login", async (
                LoginRequest request,
                AuthService authService,
                HttpContext httpContext,
                IOptions<AuthOptions> authOptions,
                ITokenService tokens,
                CancellationToken cancellationToken) =>
            {
                var result = await authService.LoginAsync(request, cancellationToken);

                return AuthResponse.Write(httpContext, result, authOptions.Value, tokens.RefreshTokenLifetime);
            })
            .WithValidation<LoginRequest>()
            .WithSummary("Sign in")
            .WithDescription(
                "Returns one generic error for every failure mode — unknown account, wrong " +
                "password, disabled or locked — so the endpoint cannot be used to enumerate accounts.")
            .Produces<AuthResult>()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", async (
                AuthService authService,
                HttpContext httpContext,
                IOptions<AuthOptions> authOptions,
                ITokenService tokens,
                CancellationToken cancellationToken,
                RefreshRequest? request = null) =>
            {
                var refreshToken = AuthResponse.ReadRefreshToken(httpContext, request, authOptions.Value);
                var result = await authService.RefreshAsync(refreshToken, cancellationToken);

                return AuthResponse.Write(httpContext, result, authOptions.Value, tokens.RefreshTokenLifetime);
            })
            .WithSummary("Exchange a refresh token for a new access token")
            .WithDescription(
                "Reads the token from the HttpOnly cookie, or from the body for non-browser " +
                "clients. Tokens rotate on every use; replaying a rotated token revokes the " +
                "whole session.")
            .Produces<AuthResult>()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (
                AuthService authService,
                HttpContext httpContext,
                IOptions<AuthOptions> authOptions,
                CancellationToken cancellationToken,
                RefreshRequest? request = null) =>
            {
                var refreshToken = AuthResponse.ReadRefreshToken(httpContext, request, authOptions.Value);

                await authService.LogoutAsync(refreshToken, cancellationToken);

                AuthResponse.ClearRefreshCookie(httpContext, authOptions.Value);

                return Results.NoContent();
            })
            .WithSummary("Sign out")
            .WithDescription("Revokes the whole refresh-token chain, ending the session on every device that shares it.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", async (AuthService authService, CancellationToken cancellationToken) =>
                Results.Ok(await authService.GetMeAsync(cancellationToken)))
            .RequireAuthorization()
            .WithSummary("Current user and their organizations")
            .WithDescription(
                "Includes the caller's permissions per organization so the UI can hide controls " +
                "it should not offer. Presentation only: the server authorizes every request.")
            .Produces<MeResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        return group;
    }
}
