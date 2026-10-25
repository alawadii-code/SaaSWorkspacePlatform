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

/// <summary>
/// Hosts the real API against a real PostgreSQL database.
/// </summary>
/// <remarks>
/// <para>
/// These are integration tests in the useful sense: the whole pipeline runs —
/// JWT authentication, the tenant resolution filter, EF Core query filters,
/// Postgres constraints and triggers. Substituting an in-memory database would
/// silently drop exactly the guarantees worth testing (partial unique indexes,
/// the composite foreign key, the audit immutability trigger).
/// </para>
/// <para>
/// Each run creates a uniquely named database and drops it afterwards, so runs
/// never collide and a failed run leaves nothing behind. The connection comes
/// from <c>WORKSPACE_TEST_POSTGRES</c> — Docker Compose, CI services and a
/// local server all work without code changes.
/// </para>
/// <para>
/// Two components are swapped: the queue runs in memory so a test can inspect
/// what was enqueued and run handlers deterministically, and email is captured
/// rather than sent. Everything else is production code.
/// </para>
/// </remarks>
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

        // Touching the server builds the host, which applies migrations to the
        // fresh database.
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

        // Added last, so these win over appsettings.json.
        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
                ["Database:MigrateOnStartup"] = "true",

                ["Jwt:SigningKey"] = "integration-tests-signing-key-at-least-32-bytes-long",
                ["Jwt:Issuer"] = "workspace-api-tests",
                ["Jwt:Audience"] = "workspace-web-tests",

                // TestServer speaks http, so the cookie cannot be Secure here.
                ["Auth:UseSecureCookies"] = "false",

                // Lets tests exercise the API-client path as well as the cookie.
                ["Auth:ReturnRefreshTokenInBody"] = "true",

                // Deterministic queue, inspectable by tests.
                ["Queue:Provider"] = "memory",
                ["Queue:PollIntervalMs"] = "50",
                ["Queue:VisibilityTimeoutSeconds"] = "60",

                // Rate limits would make repeated sign-in attempts flaky; the
                // limiter itself is still wired up, it just permits freely.
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

/// <summary>
/// One database and one API host shared by every test class. Tests isolate
/// themselves by creating their own users and organizations, which is also
/// what makes them realistic: the tenant guarantees are asserted with other
/// tenants' data present.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
