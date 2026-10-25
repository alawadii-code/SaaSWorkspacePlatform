using System.ComponentModel.DataAnnotations;

namespace Workspace.Api.Http;

/// <summary>Cookie and token-delivery settings for the HTTP layer.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Required]
    public string RefreshCookieName { get; set; } = "workspace_rt";

    /// <summary>
    /// Marks the refresh cookie Secure. Only turned off for plain-HTTP local
    /// development; startup refuses to run with it off in Production.
    /// </summary>
    public bool UseSecureCookies { get; set; } = true;

    /// <summary>
    /// Also return the refresh token in the response body. Off by default:
    /// the cookie is unreadable by scripts, a body value is not. Useful for
    /// curl and for native clients.
    /// </summary>
    public bool ReturnRefreshTokenInBody { get; set; }

    /// <summary>Path the refresh cookie is scoped to.</summary>
    public string CookiePath { get; set; } = "/api/auth";
}

/// <summary>Browser origins allowed to call the API cross-origin.</summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public const string PolicyName = "workspace-web";

    /// <summary>
    /// Explicit origins only — never a wildcard, because the API answers with
    /// credentials (the refresh cookie). In Compose the frontend is served
    /// through the same origin as the API, so this matters only for
    /// <c>vite dev</c>.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>Request-rate limits.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public const string AuthPolicy = "auth";
    public const string InvitationPolicy = "invitation-lookup";

    /// <summary>Turned off in the integration test host so tests are not rate limited.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Sign-in, registration and refresh attempts per minute per client address.</summary>
    [Range(1, 10_000)]
    public int AuthPerMinute { get; set; } = 10;

    /// <summary>Anonymous invitation lookups per minute per client address.</summary>
    [Range(1, 10_000)]
    public int InvitationLookupPerMinute { get; set; } = 20;

    /// <summary>Overall requests per minute per client address.</summary>
    [Range(1, 1_000_000)]
    public int GlobalPerMinute { get; set; } = 600;
}
