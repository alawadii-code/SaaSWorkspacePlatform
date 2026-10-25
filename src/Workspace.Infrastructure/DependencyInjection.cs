using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Workspace.Application.Abstractions;
using Workspace.Application.Invitations;
using Workspace.Infrastructure.Audit;
using Workspace.Infrastructure.Email;
using Workspace.Infrastructure.Jobs;
using Workspace.Infrastructure.Options;
using Workspace.Infrastructure.Persistence;
using Workspace.Infrastructure.Security;
using Workspace.Infrastructure.Tenancy;

namespace Workspace.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence, security, queue and mail implementations shared
    /// by the API and the worker.
    /// </summary>
    /// <remarks>
    /// Every options class is validated with <c>ValidateOnStart</c>, so a
    /// deployment missing a signing key or a connection string fails at
    /// startup with a clear message rather than at the first request.
    /// </remarks>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<QueueOptions>()
            .Bind(configuration.GetSection(QueueOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<InvitationOptions>()
            .Bind(configuration.GetSection(InvitationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        AddPersistence(services, configuration);
        AddSecurity(services);
        AddQueue(services, configuration);
        AddEmail(services, configuration);

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings__Postgres is not configured. See .env.example.");

        services.AddDbContext<WorkspaceDbContext>(builder =>
            builder.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(WorkspaceDbContext).Assembly.FullName);

                // Absorbs the brief connection failures that happen when
                // Postgres restarts or a container is still warming up.
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            }));

        // The application layer sees only the narrow IWorkspaceDb surface;
        // Infrastructure components that need the real context ask for it.
        services.AddScoped<IWorkspaceDb>(sp => sp.GetRequiredService<WorkspaceDbContext>());

        services.AddScoped<IAuditWriter, AuditWriter>();
    }

    private static void AddSecurity(IServiceCollection services)
    {
        // One instance per request, written once by the tenant middleware and
        // read by everything downstream.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());

        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
    }

    private static void AddQueue(IServiceCollection services, IConfiguration configuration)
    {
        var queueOptions = configuration.GetSection(QueueOptions.SectionName).Get<QueueOptions>()
            ?? new QueueOptions();

        if (queueOptions.UseRedis)
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var redisConfiguration = ConfigurationOptions.Parse(queueOptions.ConnectionString);

                // Start even when Redis is not up yet: enqueue failures are
                // handled by invitation reconciliation, so a cold Redis delays
                // email rather than breaking sign-up.
                redisConfiguration.AbortOnConnectFail = false;
                redisConfiguration.ConnectRetry = 5;

                return ConnectionMultiplexer.Connect(redisConfiguration);
            });

            services.AddSingleton<RedisJobQueue>();
            services.AddSingleton<IJobQueue>(sp => sp.GetRequiredService<RedisJobQueue>());
            services.AddSingleton<IJobQueueConsumer>(sp => sp.GetRequiredService<RedisJobQueue>());
        }
        else
        {
            services.AddSingleton<InMemoryJobQueue>();
            services.AddSingleton<IJobQueue>(sp => sp.GetRequiredService<InMemoryJobQueue>());
            services.AddSingleton<IJobQueueConsumer>(sp => sp.GetRequiredService<InMemoryJobQueue>());
        }
    }

    private static void AddEmail(IServiceCollection services, IConfiguration configuration)
    {
        var smtpOptions = configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>()
            ?? new SmtpOptions();

        if (smtpOptions.Enabled)
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
    }
}
