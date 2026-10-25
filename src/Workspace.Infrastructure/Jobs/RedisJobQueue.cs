using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Workspace.Application.Abstractions;
using Workspace.Application.Common;
using Workspace.Infrastructure.Options;

namespace Workspace.Infrastructure.Jobs;

/// <summary>
/// A reliable job queue on Redis lists and sorted sets.
/// </summary>
/// <remarks>
/// <para>
/// Four keys: <c>ready</c> (a FIFO list), <c>processing</c> (a sorted set
/// scored by the time each job was picked up), <c>scheduled</c> (a sorted set
/// scored by the time each job becomes runnable) and <c>dead</c> (a capped
/// list of exhausted jobs).
/// </para>
/// <para>
/// Dequeue pops from <c>ready</c> and records the job in <c>processing</c>
/// inside one Lua script, so a worker that dies immediately after the pop
/// cannot lose the job: it stays in <c>processing</c> until
/// <see cref="RecoverStuckAsync"/> returns it after the visibility timeout.
/// Delivery is therefore at-least-once, and handlers are written to be
/// idempotent.
/// </para>
/// <para>
/// Why not a library such as Hangfire or a Redis Stream consumer group: this
/// needs four operations, the semantics above are the ones the invitation flow
/// requires, and the whole implementation is small enough to read and test.
/// A system with many job types and a need for dashboards would be better
/// served by the library.
/// </para>
/// </remarks>
public sealed class RedisJobQueue : IJobQueue, IJobQueueConsumer
{
    /// <summary>
    /// Pops the oldest ready job and records it as in-flight atomically.
    /// </summary>
    private const string PopAndTrackScript =
        """
        local payload = redis.call('RPOP', KEYS[1])
        if payload then
            redis.call('ZADD', KEYS[2], ARGV[1], payload)
        end
        return payload
        """;

    /// <summary>Most dead-lettered jobs to keep for inspection.</summary>
    private const int DeadLetterRetention = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer redis;
    private readonly QueueOptions options;
    private readonly IClock clock;
    private readonly ILogger<RedisJobQueue> logger;

    public RedisJobQueue(
        IConnectionMultiplexer redis,
        IOptions<QueueOptions> options,
        IClock clock,
        ILogger<RedisJobQueue> logger)
    {
        this.redis = redis;
        this.options = options.Value;
        this.clock = clock;
        this.logger = logger;

        var prefix = this.options.KeyPrefix;
        ReadyKey = $"{prefix}:ready";
        ProcessingKey = $"{prefix}:processing";
        ScheduledKey = $"{prefix}:scheduled";
        DeadKey = $"{prefix}:dead";
    }

    private RedisKey ReadyKey { get; }

    private RedisKey ProcessingKey { get; }

    private RedisKey ScheduledKey { get; }

    private RedisKey DeadKey { get; }

    private IDatabase Database => redis.GetDatabase();

    public async Task EnqueueAsync(JobMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Database.ListLeftPushAsync(ReadyKey, Serialize(message));
    }

    public async Task EnqueueAtAsync(
        JobMessage message,
        DateTimeOffset runAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Database.SortedSetAddAsync(ScheduledKey, Serialize(message), runAt.ToUnixTimeMilliseconds());
    }

    public async Task<JobMessage?> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = clock.UtcNow.Add(timeout);
        var pollInterval = TimeSpan.FromMilliseconds(options.PollIntervalMs);

        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await Database.ScriptEvaluateAsync(
                PopAndTrackScript,
                [ReadyKey, ProcessingKey],
                [clock.UtcNow.ToUnixTimeMilliseconds()]);

            if (!result.IsNull)
            {
                var payload = (string?)result;

                if (TryDeserialize(payload, out var message))
                {
                    return message;
                }

                // Unreadable payload: drop it out of the processing set rather
                // than letting it be redelivered forever.
                logger.LogError("Discarding unreadable job payload: {Payload}", payload);
                await Database.SortedSetRemoveAsync(ProcessingKey, payload);
                continue;
            }

            if (clock.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return null;
    }

    public async Task AcknowledgeAsync(JobMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await RemoveFromProcessingAsync(message);
    }

    public async Task RetryAsync(JobMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var next = message.NextAttempt();
        var runAt = clock.UtcNow.Add(delay);

        // Schedule the next attempt first: a crash between these two steps
        // leaves the job in the processing set, where recovery finds it, rather
        // than dropping it entirely.
        await Database.SortedSetAddAsync(ScheduledKey, Serialize(next), runAt.ToUnixTimeMilliseconds());
        await RemoveFromProcessingAsync(message);

        logger.LogWarning(
            "Job {JobId} ({JobType}) failed; attempt {Attempt} scheduled in {Delay}.",
            message.Id,
            message.Type,
            next.Attempt,
            delay);
    }

    public async Task DeadLetterAsync(JobMessage message, string error, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var record = JsonSerializer.Serialize(
            new { message.Id, message.Type, message.Payload, message.Attempt, error, failedAt = clock.UtcNow },
            SerializerOptions);

        await Database.ListLeftPushAsync(DeadKey, record);
        await Database.ListTrimAsync(DeadKey, 0, DeadLetterRetention - 1);
        await RemoveFromProcessingAsync(message);

        logger.LogError(
            "Job {JobId} ({JobType}) dead-lettered after {Attempts} attempts: {Error}",
            message.Id,
            message.Type,
            message.Attempt,
            error);
    }

    public async Task<int> PromoteScheduledAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = clock.UtcNow.ToUnixTimeMilliseconds();

        var due = await Database.SortedSetRangeByScoreAsync(
            ScheduledKey,
            double.NegativeInfinity,
            now,
            take: 100);

        var promoted = 0;

        foreach (var payload in due)
        {
            // Removing first acts as the claim: if two workers see the same
            // due job, only the one whose remove returns true re-queues it.
            if (await Database.SortedSetRemoveAsync(ScheduledKey, payload))
            {
                await Database.ListLeftPushAsync(ReadyKey, payload);
                promoted++;
            }
        }

        return promoted;
    }

    public async Task<int> RecoverStuckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cutoff = clock.UtcNow
            .AddSeconds(-options.VisibilityTimeoutSeconds)
            .ToUnixTimeMilliseconds();

        var stuck = await Database.SortedSetRangeByScoreAsync(
            ProcessingKey,
            double.NegativeInfinity,
            cutoff,
            take: 100);

        var recovered = 0;

        foreach (var payload in stuck)
        {
            if (!await Database.SortedSetRemoveAsync(ProcessingKey, payload))
            {
                continue;
            }

            if (!TryDeserialize(payload, out var message))
            {
                continue;
            }

            var next = message.NextAttempt();

            if (RetryPolicy.ShouldRetry(next.Attempt))
            {
                await Database.ListLeftPushAsync(ReadyKey, Serialize(next));
                recovered++;

                logger.LogWarning(
                    "Recovered stuck job {JobId} ({JobType}); re-queued as attempt {Attempt}.",
                    message.Id,
                    message.Type,
                    next.Attempt);
            }
            else
            {
                await DeadLetterAsync(next, "Visibility timeout exceeded and attempts exhausted.", cancellationToken);
            }
        }

        return recovered;
    }

    /// <summary>
    /// Removes a job from the in-flight set.
    /// </summary>
    /// <remarks>
    /// The fast path removes the exact payload, which works because the same
    /// instance re-serializes identically. The fallback exists so a
    /// serialization difference can never strand a job in the processing set:
    /// it finds the entry with a matching job id and removes that.
    /// </remarks>
    private async Task RemoveFromProcessingAsync(JobMessage message)
    {
        if (await Database.SortedSetRemoveAsync(ProcessingKey, Serialize(message)))
        {
            return;
        }

        var inFlight = await Database.SortedSetRangeByScoreAsync(ProcessingKey, take: 500);

        foreach (var payload in inFlight)
        {
            if (TryDeserialize(payload, out var candidate) && candidate.Id == message.Id)
            {
                await Database.SortedSetRemoveAsync(ProcessingKey, payload);
                return;
            }
        }
    }

    private static string Serialize(JobMessage message) =>
        JsonSerializer.Serialize(message, SerializerOptions);

    private static bool TryDeserialize(string? payload, out JobMessage message)
    {
        if (!string.IsNullOrEmpty(payload))
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<JobMessage>(payload, SerializerOptions);

                if (deserialized is not null)
                {
                    message = deserialized;
                    return true;
                }
            }
            catch (JsonException)
            {
                // Fall through to the failure result below.
            }
        }

        message = null!;
        return false;
    }
}
