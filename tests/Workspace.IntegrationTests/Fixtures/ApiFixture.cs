using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Workspace.Application.Abstractions;
using Workspace.Infrastructure.Jobs;
using Xunit;

namespace Workspace.IntegrationTests.Fixtures;

/// <summary>Hosts the real API against a real PostgreSQL database.</summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string DefaultAdminConnectionString =
        "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";

    private readonly string databaseName = $"workspace_test_{Guid.NewGuid():N}";

    /// <summary>Connection string for this run's throwaway database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>The queue the API writes to, so tests can read job payloads.</summary>
    public InMemoryJobQueue Queue => Services.GetRequiredService<InMemoryJobQueue>();

    /// <summary>Emails the worker "sent" during the test.</summary>
    public CapturingEmailSender Emails => Services.GetRequiredService<CapturingEmailSender>();

    private static string AdminConnectionString =>
        Environment.GetEnvironmentVariable("WORKSPACE_TEST_POSTGRES") ?? DefaultAdminConnectionString;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();

        await using (var create = new NpgsqlCommand($"""CREATE DATABASE "{databaseName}";""", connection))
        {
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = databaseName,
        }.ConnectionString;

        _ = Server;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();

        await using var drop = new NpgsqlCommand(
            $"""DROP DATABASE IF EXISTS "{databaseName}" WITH (FORCE);""",
            connection);

        await drop.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // ngl added last, so these win over appsettings.json.
        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
                ["Database:MigrateOnStartup"] = "true",

                ["Jwt:SigningKey"] = "integration-tests-signing-key-at-least-32-bytes-long",
                ["Jwt:Issuer"] = "workspace-api-tests",
                ["Jwt:Audience"] = "workspace-web-tests",

                // ok sOoo testServer speaks http, so the cookie cant be Secure here.
                ["Auth:UseSecureCookies"] = "false",

                // ngl lets tests exercise the API-client path as well as the cookie.
                ["Auth:ReturnRefreshTokenInBody"] = "true",

                // basically deterministic queue, inspectable by tests.
                ["Queue:Provider"] = "memory",
                ["Queue:PollIntervalMs"] = "50",
                ["Queue:VisibilityTimeoutSeconds"] = "60",

                ["RateLimiting:Enabled"] = "false",

                ["Smtp:Enabled"] = "false",
                ["Invitations:LifetimeDays"] = "7",
                ["Invitations:AcceptUrlTemplate"] = "http://localhost:5173/invitations/accept?token={token}",
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<CapturingEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<CapturingEmailSender>());
        });
    }
}

/// <summary>One database and one API host shared by evry test class.</summary>
[CollectionDefinition(ApiCollection.Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
