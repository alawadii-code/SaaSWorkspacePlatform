using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Workspace.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations on startup.
/// </summary>
/// <remarks>
/// <para>
/// Convenient for local development and for Compose, where the API container
/// usually wins the race to start before Postgres finishes initializing — the
/// retry loop below absorbs that.
/// </para>
/// <para>
/// Trade-off worth stating: running migrations from the application process
/// does not scale to several replicas starting at once, and it gives the
/// application's database user DDL rights. A production deployment should run
/// <c>dotnet ef database update</c> (or the generated idempotent SQL script)
/// as a separate step in the release pipeline and set
/// <c>Database:MigrateOnStartup=false</c>.
/// </para>
/// </remarks>
public static class DatabaseMigrator
{
    public static async Task MigrateAsync(
        IServiceProvider services,
        int maxAttempts = 10,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseMigrator));

        var db = scope.ServiceProvider.GetRequiredService<WorkspaceDbContext>();

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(cancellationToken);

                logger.LogInformation("Database schema is up to date.");
                return;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(attempt * 2, 10));

                logger.LogWarning(
                    "Database not ready (attempt {Attempt}/{MaxAttempts}): {Message}. Retrying in {Delay}.",
                    attempt,
                    maxAttempts,
                    ex.Message,
                    delay);

                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Could not apply database migrations after {maxAttempts} attempts.");
    }

    private static bool IsTransient(Exception exception) => exception switch
    {
        NpgsqlException { IsTransient: true } => true,
        NpgsqlException => true,
        TimeoutException => true,
        _ => exception.InnerException is not null && IsTransient(exception.InnerException),
    };
}
