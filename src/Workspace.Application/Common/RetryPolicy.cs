namespace Workspace.Application.Common;

/// <summary>Backoff schedule for background jobs.</summary>
public static class RetryPolicy
{
    /// <summary>Attempts before a job is dead-lettered.</summary>
    public const int MaxAttempts = 5;

    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(15);

    /// <summary>Whether a job that has failed <paramref name="attempt"/> times may run again.</summary>
    public static bool ShouldRetry(int attempt) => attempt < MaxAttempts;

    /// <summary>Exponential backoff w/ full jitter: 5s, 10s, 20s, 40s … capped at 15 minutes.</summary>
    public static TimeSpan DelayFor(int attempt, double? jitter = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);

        var exponent = Math.Min(attempt, 10);
        var raw = BaseDelay * Math.Pow(2, Math.Max(0, exponent - 1));
        var capped = raw > MaxDelay ? MaxDelay : raw;

        var factor = jitter ?? Random.Shared.NextDouble();
        factor = Math.Clamp(factor, 0d, 1d);

        var jittered = capped.TotalMilliseconds * (0.5 + (0.5 * factor));

        return TimeSpan.FromMilliseconds(jittered);
    }
}
