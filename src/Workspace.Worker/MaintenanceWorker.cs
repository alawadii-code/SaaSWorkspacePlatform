using Workspace.Application.Abstractions;
using Workspace.Application.Invitations;

namespace Workspace.Worker;

/// <summary>The periodic half of the background work.</summary>
public sealed class MaintenanceWorker(
    IJobQueueConsumer queue,
    IServiceScopeFactory scopeFactory,
    ILogger<MaintenanceWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Maintenance worker started; interval {Interval}.", Interval);

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Maintenance pass failed.");
            }
        }

        logger.LogInformation("Maintenance worker stopping.");
    }

    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        var promoted = await queue.PromoteScheduledAsync(cancellationToken);
        var recovered = await queue.RecoverStuckAsync(cancellationToken);

        using var scope = scopeFactory.CreateScope();
        var invitations = scope.ServiceProvider.GetRequiredService<InvitationService>();

        var expired = await invitations.ExpireStaleAsync(cancellationToken: cancellationToken);
        var requeued = await invitations.ReconcileUndeliveredAsync(cancellationToken: cancellationToken);

        if (promoted + recovered + expired + requeued > 0)
        {
            logger.LogInformation(
                "Maintenance pass: {Promoted} promoted, {Recovered} recovered, {Expired} expired, {Requeued} re-queued.",
                promoted,
                recovered,
                expired,
                requeued);
        }
    }
}
