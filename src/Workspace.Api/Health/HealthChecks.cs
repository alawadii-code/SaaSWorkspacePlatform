using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Workspace.Infrastructure.Persistence;

namespace Workspace.Api.Health;

/// <summary>
/// Readiness check for Postgres. Liveness deliberately does not include this:
/// an orchestrator should not restart a healthy API because the database is
/// briefly unavailable, it should stop sending it traffic.
/// </summary>
public sealed class DatabaseHealthCheck(WorkspaceDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Database reachable.")
                : HealthCheckResult.Unhealthy("Database unreachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database check failed.", ex);
        }
    }
}

/// <summary>
/// Readiness check for Redis. Degraded rather than unhealthy: with Redis down
/// the API still serves every request, and invitation emails are reconciled
/// once it returns.
/// </summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync();

            return HealthCheckResult.Healthy("Redis reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Redis unreachable; queued email will be delayed.", ex);
        }
    }
}
