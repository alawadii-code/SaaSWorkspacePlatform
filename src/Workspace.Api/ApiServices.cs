using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Workspace.Api.Health;
using Workspace.Api.Http;
using Workspace.Application.Abstractions;
using Workspace.Infrastructure.Options;

namespace Workspace.Api;

public static class ApiServices
{
    /// <summary>The sample key shipped in <c>.env.example</c>.</summary>
    public const string PlaceholderSigningKey = "replace-with-a-32-byte-minimum-random-secret";

    public static IServiceCollection AddApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<CorsOptions>(configuration.GetSection(CorsOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddProblemDetails();
        services.AddExceptionHandler<AppExceptionHandler>();

        services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;

            if (configuration.GetValue("ForwardedHeaders:TrustAllProxies", defaultValue: true))
            {
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
            }
        });

        AddAuthentication(services, configuration, environment);
        AddCors(services, configuration);
        AddRateLimiting(services, configuration);
        AddHealthChecks(services, configuration);

        services.AddOpenApi();

        return services;
    }

    private static void AddAuthentication(
        IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        GuardProductionConfiguration(configuration, environment, jwt);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,

                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                };
            });

        services.AddAuthorization();
    }

    /// <summary>Fails startup on configuration that is fine locally and dangerous in production.</summary>
    private static void GuardProductionConfiguration(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        JwtOptions jwt)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(jwt.SigningKey) ||
            string.Equals(jwt.SigningKey, PlaceholderSigningKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Jwt__SigningKey is unset or still the example value. Generate one with " +
                "`openssl rand -base64 48` and supply it through the environment.");
        }

        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

        if (!auth.UseSecureCookies)
        {
            throw new InvalidOperationException(
                "Auth__UseSecureCookies must stay true outside Development: the refresh cookie " +
                "would otherwise be sent over plain HTTP.");
        }
    }

    private static void AddCors(IServiceCollection services, IConfiguration configuration)
    {
        var cors = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();

        services.AddCors(options => options.AddPolicy(CorsOptions.PolicyName, policy =>
        {
            if (cors.AllowedOrigins.Length == 0)
            {
                policy.WithOrigins().AllowCredentials();
                return;
            }

            policy
                .WithOrigins(cors.AllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders(HttpCurrentUser.CorrelationHeader);
        }));
    }

    private static void AddRateLimiting(IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
            ?? new RateLimitingOptions();

        var authPermit = limits.Enabled ? limits.AuthPerMinute : int.MaxValue;
        var invitationPermit = limits.Enabled ? limits.InvitationLookupPerMinute : int.MaxValue;
        var globalPermit = limits.Enabled ? limits.GlobalPerMinute : int.MaxValue;

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitingOptions.AuthPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = authPermit,
                        Window = TimeSpan.FromMinutes(1),
                    }));

            options.AddPolicy(RateLimitingOptions.InvitationPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = invitationPermit,
                        Window = TimeSpan.FromMinutes(1),
                    }));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = globalPermit,
                        Window = TimeSpan.FromMinutes(1),
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too Many Requests",
                    Detail = "Rate limit exceeded. Try again shortly.",
                    Type = "https://docs.workspace.local/errors/rate_limited",
                };

                problem.Extensions["code"] = "rate_limited";

                await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
            };
        });
    }

    /// <summary>Partitions limits by authenticated user when there is one, otherwise by client address, so one noisy client cannot exhaust everyone's budget.</summary>
    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst("sub")?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";

    private static void AddHealthChecks(IServiceCollection services, IConfiguration configuration)
    {
        var queue = configuration.GetSection(QueueOptions.SectionName).Get<QueueOptions>() ?? new QueueOptions();

        var builder = services
            .AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(
                "database",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"]);

        if (queue.UseRedis)
        {
            builder.AddCheck<RedisHealthCheck>(
                "redis",
                failureStatus: HealthStatus.Degraded,
                tags: ["ready"]);
        }
    }
}
