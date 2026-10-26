using Workspace.Application.Abstractions;
using Workspace.Application.Auth;

namespace Workspace.Api.Http;

/// <summary>Writes authentication results: access token in the body, refresh token in an HttpOnly cookie.</summary>
public static class AuthResponse
{
    public static IResult Write(
        HttpContext httpContext,
        AuthResult result,
        AuthOptions authOptions,
        TimeSpan refreshLifetime)
    {
        if (result.RefreshToken is not null)
        {
            httpContext.Response.Cookies.Append(
                authOptions.RefreshCookieName,
                result.RefreshToken,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = authOptions.UseSecureCookies,

                    SameSite = SameSiteMode.Lax,
                    Path = authOptions.CookiePath,
                    Expires = DateTimeOffset.UtcNow.Add(refreshLifetime),
                    IsEssential = true,
                });
        }

        return Results.Ok(authOptions.ReturnRefreshTokenInBody
            ? result
            : result with { RefreshToken = null });
    }

    /// <summary>Removes the refresh cookie on sign-out.</summary>
    public static void ClearRefreshCookie(HttpContext httpContext, AuthOptions authOptions) =>
        httpContext.Response.Cookies.Delete(
            authOptions.RefreshCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = authOptions.UseSecureCookies,
                SameSite = SameSiteMode.Lax,
                Path = authOptions.CookiePath,
            });

    /// <summary>Reads the refresh token from the request body if present, otherwise from the cookie.</summary>
    public static string? ReadRefreshToken(
        HttpContext httpContext,
        RefreshRequest? body,
        AuthOptions authOptions)
    {
        if (!string.IsNullOrWhiteSpace(body?.RefreshToken))
        {
            return body.RefreshToken;
        }

        return httpContext.Request.Cookies.TryGetValue(authOptions.RefreshCookieName, out var cookie)
            ? cookie
            : null;
    }
}

/// <summary>Adds response headers that cost nuthin and remove whole classes of browser-side risk.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

        headers["Cache-Control"] = "no-store";

        await next(context);
    }
}
