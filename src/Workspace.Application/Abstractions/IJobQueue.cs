namespace Workspace.Application.Abstractions;

/// <summary>A unit of deferred work.</summary>
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

/// <summary>Consumer side, used by the worker.</summary>
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

    /// <summary>Returns messages whose handler nvr acknowledged them — a worker that crashed mid-job — to the ready queue once their visibility timeout has passed.</summary>
    Task<int> RecoverStuckAsync(CancellationToken cancellationToken = default);
}

/// <summary>Handles one job type.</summary>
public interface IJobHandler
{
    string JobType { get; }

    Task HandleAsync(JobMessage message, CancellationToken cancellationToken);
}
