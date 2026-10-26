using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Workspace.Application.Abstractions;
using Workspace.Application.Common;
using Workspace.Infrastructure.Options;

namespace Workspace.Infrastructure.Jobs;

/// <summary>A reliable job queue on Redis lists and sorted sets.</summary>
public sealed class RedisJobQueue : IJobQueue, IJobQueueConsumer
{
    /// <summary>Pops the oldest ready job and records it as in-flight atomically.</summary>
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

    /// <summary>Removes a job from the in-flight set.</summary>
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
                // Fall thru to the failure result below lol
            }
        }

        message = null!;
        return false;
    }
}
