using System.ComponentModel.DataAnnotations;

namespace Workspace.Infrastructure.Options;

/// <summary>Access- and refresh-token settings.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC-SHA256 signing key, supplied by configuration or environment.</summary>
    [Required(AllowEmptyStrings = false)]
    [MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters.")]
    public string SigningKey { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = "workspace-api";

    [Required]
    public string Audience { get; set; } = "workspace-web";

    /// <summary>Deliberately short: permissions are re-read from the database on evry request, but identity is not, so a disabled account keeps working until its access token expires.</summary>
    [Range(1, 120)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenMinutes);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenDays);
}

/// <summary>Background queue settings.</summary>
public sealed class QueueOptions
{
    public const string SectionName = "Queue";

    public const string RedisProvider = "redis";
    public const string MemoryProvider = "memory";

    /// <summary><c>redis</c> for the real queue, <c>memory</c> for tests and for running the API w/o Redis.</summary>
    public string Provider { get; set; } = RedisProvider;

    public string ConnectionString { get; set; } = "localhost:6379";

    public string KeyPrefix { get; set; } = "workspace:jobs";

    /// <summary>How long the worker waits btwn polls when the queue is empty.</summary>
    [Range(50, 10_000)]
    public int PollIntervalMs { get; set; } = 500;

    /// <summary>How long a job may stay in the processing set before another worker may take it over.</summary>
    [Range(10, 3600)]
    public int VisibilityTimeoutSeconds { get; set; } = 120;

    public bool UseRedis => string.Equals(Provider, RedisProvider, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Outbound mail settings.</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>When false, emails are written to the log instead of being sent.</summary>
    public bool Enabled { get; set; }

    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 1025;

    /// <summary>Upgrade to TLS when the server offers it.</summary>
    public bool UseStartTls { get; set; }

    public string? Username { get; set; }

    /// <summary>Supplied by environment or secret store — nvr committed.</summary>
    public string? Password { get; set; }

    [Required]
    public string FromAddress { get; set; } = "no-reply@workspace.local";

    [Required]
    public string FromName { get; set; } = "Workspace";

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 15;
}
