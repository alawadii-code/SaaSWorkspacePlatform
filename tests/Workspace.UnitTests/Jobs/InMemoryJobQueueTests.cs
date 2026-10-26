using Microsoft.Extensions.Options;
using Workspace.Application.Abstractions;
using Workspace.Application.Common;
using Workspace.Infrastructure.Jobs;
using Workspace.Infrastructure.Options;
using Xunit;

namespace Workspace.UnitTests.Jobs;

/// <summary>Queue semantics — in-flight tracking, retry scheduling, visibility-timeout recovery and dead-lettering — asserted against the in-memory implementation, which shares its contract with the Redis one.</summary>
public class InMemoryJobQueueTests
{
    private readonly FixedClock clock = new();
    private readonly QueueOptions options = new() { PollIntervalMs = 50, VisibilityTimeoutSeconds = 120 };

    private InMemoryJobQueue CreateQueue() => new(Options.Create(options), clock);

    private JobMessage CreateJob(string payload = "{}") =>
        JobMessage.Create(JobTypes.SendInvitationEmail, payload, clock.UtcNow);

    [Fact]
    public async Task Enqueued_jobs_come_back_in_order()
    {
        var queue = CreateQueue();
        var first = CreateJob("""{"n":1}""");
        var second = CreateJob("""{"n":2}""");

        await queue.EnqueueAsync(first);
        await queue.EnqueueAsync(second);

        Assert.Equal(first.Id, (await queue.DequeueAsync(TimeSpan.Zero))!.Id);
        Assert.Equal(second.Id, (await queue.DequeueAsync(TimeSpan.Zero))!.Id);
    }

    [Fact]
    public async Task Dequeue_returns_null_when_empty() =>
        Assert.Null(await CreateQueue().DequeueAsync(TimeSpan.Zero));

    [Fact]
    public async Task Enqueued_jobs_are_recorded_for_inspection()
    {
        var queue = CreateQueue();
        var job = CreateJob("""{"invitationId":"x"}""");

        await queue.EnqueueAsync(job);

        Assert.Single(queue.Enqueued);
        Assert.Equal(job.Payload, queue.Enqueued[0].Payload);
    }

    [Fact]
    public async Task Acknowledged_jobs_are_not_redelivered()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(CreateJob());

        var job = await queue.DequeueAsync(TimeSpan.Zero);
        await queue.AcknowledgeAsync(job!);

        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, await queue.RecoverStuckAsync());
        Assert.Null(await queue.DequeueAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task Retry_schedules_the_next_attempt_and_is_not_immediately_ready()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(CreateJob());

        var job = await queue.DequeueAsync(TimeSpan.Zero);
        await queue.RetryAsync(job!, TimeSpan.FromSeconds(30));

        Assert.Equal(0, await queue.PromoteScheduledAsync());
        Assert.Null(await queue.DequeueAsync(TimeSpan.Zero));

        clock.Advance(TimeSpan.FromSeconds(31));

        Assert.Equal(1, await queue.PromoteScheduledAsync());

        var retried = await queue.DequeueAsync(TimeSpan.Zero);

        Assert.NotNull(retried);
        Assert.Equal(job!.Id, retried.Id);
        Assert.Equal(1, retried.Attempt);
    }

    /// <summary>A job whose worker died is returned to the queue once its visibility timeout passes.</summary>
    [Fact]
    public async Task Stuck_jobs_are_recovered_after_the_visibility_timeout()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(CreateJob());

        var job = await queue.DequeueAsync(TimeSpan.Zero);

        // Simulates a worker that never acknowledged.
        Assert.Equal(0, await queue.RecoverStuckAsync());

        clock.Advance(TimeSpan.FromSeconds(options.VisibilityTimeoutSeconds + 1));

        Assert.Equal(1, await queue.RecoverStuckAsync());

        var recovered = await queue.DequeueAsync(TimeSpan.Zero);

        Assert.NotNull(recovered);
        Assert.Equal(job!.Id, recovered.Id);
        Assert.Equal(1, recovered.Attempt);
    }

    [Fact]
    public async Task Dead_lettered_jobs_are_kept_with_their_error()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(CreateJob());

        var job = await queue.DequeueAsync(TimeSpan.Zero);
        await queue.DeadLetterAsync(job!, "SMTP server refused the connection.");

        var dead = Assert.Single(queue.DeadLettered);

        Assert.Equal(job!.Id, dead.Message.Id);
        Assert.Contains("SMTP", dead.Error, StringComparison.Ordinal);
        Assert.Null(await queue.DequeueAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task Recovery_dead_letters_a_job_that_has_exhausted_its_attempts()
    {
        var queue = CreateQueue();

        var exhausted = CreateJob() with { Attempt = RetryPolicy.MaxAttempts - 1 };
        await queue.EnqueueAsync(exhausted);
        await queue.DequeueAsync(TimeSpan.Zero);

        clock.Advance(TimeSpan.FromSeconds(options.VisibilityTimeoutSeconds + 1));

        Assert.Equal(0, await queue.RecoverStuckAsync());
        Assert.Single(queue.DeadLettered);
    }

    [Fact]
    public async Task Scheduled_jobs_wait_for_their_run_time()
    {
        var queue = CreateQueue();

        await queue.EnqueueAtAsync(CreateJob(), clock.UtcNow.AddMinutes(5));

        Assert.Equal(0, await queue.PromoteScheduledAsync());

        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(1, await queue.PromoteScheduledAsync());
        Assert.Equal(1, queue.ReadyCount);
    }

    [Fact]
    public void JobMessage_NextAttempt_increments_without_changing_identity()
    {
        var job = CreateJob();
        var next = job.NextAttempt();

        Assert.Equal(job.Id, next.Id);
        Assert.Equal(job.Payload, next.Payload);
        Assert.Equal(job.Attempt + 1, next.Attempt);
    }
}
