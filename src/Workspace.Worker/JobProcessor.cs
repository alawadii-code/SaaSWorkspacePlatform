using Workspace.Application.Abstractions;
using Workspace.Application.Common;

namespace Workspace.Worker;

/// <summary>Consumes the job queue: one scope per job, retry w/ backoff on failure, dead-letter once attempts are exhausted.</summary>
public sealed class JobProcessor(
    IJobQueueConsumer queue,
    IServiceScopeFactory scopeFactory,
    ILogger<JobProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan DequeueTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LoopErrorBackoff = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job processor started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = await queue.DequeueAsync(DequeueTimeout, stoppingToken);

                if (message is null)
                {
                    continue;
                }

                await ProcessAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job loop error; retrying in {Backoff}.", LoopErrorBackoff);

                await Task.Delay(LoopErrorBackoff, stoppingToken);
            }
        }

        logger.LogInformation("Job processor stopping.");
    }

    private async Task ProcessAsync(JobMessage message, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var handler = scope.ServiceProvider
            .GetServices<IJobHandler>()
            .FirstOrDefault(candidate => string.Equals(candidate.JobType, message.Type, StringComparison.Ordinal));

        if (handler is null)
        {
            await queue.DeadLetterAsync(message, $"No handler registered for job type '{message.Type}'.", cancellationToken);
            return;
        }

        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["JobId"] = message.Id,
            ["JobType"] = message.Type,
            ["Attempt"] = message.Attempt,
        });

        try
        {
            await handler.HandleAsync(message, cancellationToken);
            await queue.AcknowledgeAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var nextAttempt = message.Attempt + 1;

            if (RetryPolicy.ShouldRetry(nextAttempt))
            {
                await queue.RetryAsync(message, RetryPolicy.DelayFor(nextAttempt), cancellationToken);
            }
            else
            {
                await queue.DeadLetterAsync(
                    message,
                    $"{ex.GetType().Name}: {ex.Message}",
                    cancellationToken);
            }
        }
    }
}
