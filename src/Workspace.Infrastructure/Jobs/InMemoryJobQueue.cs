using Microsoft.Extensions.Options;
using Workspace.Application.Abstractions;
using Workspace.Application.Common;
using Workspace.Infrastructure.Options;

namespace Workspace.Infrastructure.Jobs;

/// <summary>
/// In-process implementation of the same queue contract.
/// </summary>
/// <remarks>
/// Two uses. Integration tests swap it in so they can assert on what was
/// enqueued and drive the worker deterministically — including reading the
/// invitation token out of the job payload, which is the only place it exists
/// outside the email. And <c>Queue:Provider=memory</c> lets the API run
/// without Redis for a quick look around, at the cost of losing queued work
/// when the process stops.
/// </remarks>
public sealed class InMemoryJobQueue(IOptions<QueueOptions> options, IClock clock)
    : IJobQueue, IJobQueueConsumer
{
    private readonly Lock gate = new();
    private readonly Queue<JobMessage> ready = new();
    private readonly List<(DateTimeOffset RunAt, JobMessage Message)> scheduled = [];
    private readonly List<(DateTimeOffset PickedUpAt, JobMessage Message)> processing = [];
    private readonly List<(JobMessage Message, string Error)> dead = [];
    private readonly List<JobMessage> enqueued = [];

    /// <summary>Everything ever enqueued, in order. For test assertions.</summary>
    public IReadOnlyList<JobMessage> Enqueued
    {
        get
        {
            lock (gate)
            {
                return [.. enqueued];
            }
        }
    }

    /// <summary>Jobs that exhausted their attempts, with the final error.</summary>
    public IReadOnlyList<(JobMessage Message, string Error)> DeadLettered
    {
        get
        {
            lock (gate)
            {
                return [.. dead];
            }
        }
    }

    public int ReadyCount
    {
        get
        {
            lock (gate)
            {
                return ready.Count;
            }
        }
    }

    public Task EnqueueAsync(JobMessage message, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            ready.Enqueue(message);
            enqueued.Add(message);
        }

        return Task.CompletedTask;
    }

    public Task EnqueueAtAsync(JobMessage message, DateTimeOffset runAt, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            scheduled.Add((runAt, message));
            enqueued.Add(message);
        }

        return Task.CompletedTask;
    }

    public async Task<JobMessage?> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = clock.UtcNow.Add(timeout);
        var pollInterval = TimeSpan.FromMilliseconds(Math.Min(options.Value.PollIntervalMs, 50));

        while (true)
        {
            lock (gate)
            {
                if (ready.TryDequeue(out var message))
                {
                    processing.Add((clock.UtcNow, message));
                    return message;
                }
            }

            if (clock.UtcNow >= deadline || cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    public Task AcknowledgeAsync(JobMessage message, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            processing.RemoveAll(entry => entry.Message.Id == message.Id);
        }

        return Task.CompletedTask;
    }

    public Task RetryAsync(JobMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            processing.RemoveAll(entry => entry.Message.Id == message.Id);
            scheduled.Add((clock.UtcNow.Add(delay), message.NextAttempt()));
        }

        return Task.CompletedTask;
    }

    public Task DeadLetterAsync(JobMessage message, string error, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            processing.RemoveAll(entry => entry.Message.Id == message.Id);
            dead.Add((message, error));
        }

        return Task.CompletedTask;
    }

    public Task<int> PromoteScheduledAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var now = clock.UtcNow;
            var due = scheduled.Where(entry => entry.RunAt <= now).ToList();

            foreach (var entry in due)
            {
                scheduled.Remove(entry);
                ready.Enqueue(entry.Message);
            }

            return Task.FromResult(due.Count);
        }
    }

    public Task<int> RecoverStuckAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var cutoff = clock.UtcNow.AddSeconds(-options.Value.VisibilityTimeoutSeconds);
            var stuck = processing.Where(entry => entry.PickedUpAt <= cutoff).ToList();
            var recovered = 0;

            foreach (var entry in stuck)
            {
                processing.Remove(entry);
                var next = entry.Message.NextAttempt();

                if (RetryPolicy.ShouldRetry(next.Attempt))
                {
                    ready.Enqueue(next);
                    recovered++;
                }
                else
                {
                    dead.Add((next, "Visibility timeout exceeded and attempts exhausted."));
                }
            }

            return Task.FromResult(recovered);
        }
    }
}
