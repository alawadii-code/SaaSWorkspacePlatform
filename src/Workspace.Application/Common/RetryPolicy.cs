namespace Workspace.Application.Common;

/// <summary>
/// Backoff schedule for background jobs. A pure function, so the worker's
/// retry behaviour is unit-tested without sleeping or touching Redis.
/// </summary>
public static class RetryPolicy
{
    /// <summary>Attempts before a job is dead-lettered.</summary>
    public const int MaxAttempts = 5;

    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(15);

    /// <summary>Whether a job that has failed <paramref name="attempt"/> times may run again.</summary>
    public static bool ShouldRetry(int attempt) => attempt < MaxAttempts;

    /// <summary>
    /// Exponential backoff with full jitter: 5s, 10s, 20s, 40s … capped at 15
    /// minutes. Jitter keeps a batch of invitations that failed together from
    /// retrying in lockstep and hammering the mail server.
    /// </summary>
    /// <param name="attempt">Attempts already made (1 after the first failure).</param>
    /// <param name="jitter">
    /// Fraction in [0,1) used to spread the delay; defaults to a random value.
    /// Tests pass a fixed value to make the result deterministic.
    /// </param>
    public static TimeSpan DelayFor(int attempt, double? jitter = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);

        var exponent = Math.Min(attempt, 10);
        var raw = BaseDelay * Math.Pow(2, Math.Max(0, exponent - 1));
        var capped = raw > MaxDelay ? MaxDelay : raw;

        var factor = jitter ?? Random.Shared.NextDouble();
        factor = Math.Clamp(factor, 0d, 1d);

        // Full jitter over the lower half keeps delays monotonic in expectation
        // while still spreading simultaneous failures apart.
        var jittered = capped.TotalMilliseconds * (0.5 + (0.5 * factor));

        return TimeSpan.FromMilliseconds(jittered);
    }
}
