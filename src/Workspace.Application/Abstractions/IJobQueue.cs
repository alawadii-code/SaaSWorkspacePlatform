namespace Workspace.Application.Abstractions;

/// <summary>
/// A unit of deferred work. <see cref="Payload"/> is JSON; <see cref="Type"/>
/// selects the handler.
/// </summary>
/// <param name="Id">Stable id, carried through retries for log correlation.</param>
/// <param name="Type">Handler key, from <see cref="JobTypes"/>.</param>
/// <param name="Payload">JSON-serialized job arguments.</param>
/// <param name="Attempt">Number of delivery attempts so far.</param>
/// <param name="EnqueuedAt">When the job was first enqueued.</param>
public sealed record JobMessage(
    Guid Id,
    string Type,
    string Payload,
    int Attempt,
    DateTimeOffset EnqueuedAt)
{
    public static JobMessage Create(string type, string payload, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), type, payload, Attempt: 0, EnqueuedAt: now);

    public JobMessage NextAttempt() => this with { Attempt = Attempt + 1 };
}

/// <summary>Known job types.</summary>
public static class JobTypes
{
    public const string SendInvitationEmail = "invitation.send_email";
}

/// <summary>Producer side of the job queue, used by request handlers.</summary>
public interface IJobQueue
{
    Task EnqueueAsync(JobMessage message, CancellationToken cancellationToken = default);

    /// <summary>Enqueues a job that must not run before <paramref name="runAt"/>.</summary>
    Task EnqueueAtAsync(JobMessage message, DateTimeOffset runAt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Consumer side, used by the worker.
/// </summary>
/// <remarks>
/// Reliable handoff: a dequeued message is held in a processing list until it
/// is acknowledged, so a worker that crashes mid-job does not lose it.
/// </remarks>
public interface IJobQueueConsumer
{
    /// <summary>Blocks up to <paramref name="timeout"/> for the next message.</summary>
    Task<JobMessage?> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Marks a message as completed and removes it from the processing list.</summary>
    Task AcknowledgeAsync(JobMessage message, CancellationToken cancellationToken = default);

    /// <summary>Schedules another attempt after <paramref name="delay"/>.</summary>
    Task RetryAsync(JobMessage message, TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>Moves a message to the dead-letter list after its attempts are exhausted.</summary>
    Task DeadLetterAsync(JobMessage message, string error, CancellationToken cancellationToken = default);

    /// <summary>Promotes scheduled messages whose run time has arrived.</summary>
    Task<int> PromoteScheduledAsync(CancellationToken cancellationToken = default);
}

/// <summary>Handles one job type.</summary>
public interface IJobHandler
{
    string JobType { get; }

    Task HandleAsync(JobMessage message, CancellationToken cancellationToken);
}
