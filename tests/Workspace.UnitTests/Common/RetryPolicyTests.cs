using Workspace.Application.Common;
using Xunit;

namespace Workspace.UnitTests.Common;

public class RetryPolicyTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public void ShouldRetry_stops_at_the_attempt_limit(int attempt, bool expected) =>
        Assert.Equal(expected, RetryPolicy.ShouldRetry(attempt));

    /// <summary>With jitter pinned, the schedule is deterministic and doubles each attempt: 5s, 10s, 20s, 40s.</summary>
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    public void DelayFor_doubles_each_attempt(int attempt, int expectedSeconds)
    {
        var delay = RetryPolicy.DelayFor(attempt, jitter: 1d);

        Assert.Equal(expectedSeconds, delay.TotalSeconds, precision: 1);
    }

    [Fact]
    public void DelayFor_is_capped()
    {
        var delay = RetryPolicy.DelayFor(attempt: 50, jitter: 1d);

        Assert.Equal(TimeSpan.FromMinutes(15), delay);
    }

    /// <summary>Jitter spreads simultaneous failures apart; it must never produce a zero or negative delay, which would turn a retry into a tight loop.</summary>
    [Theory]
    [InlineData(0d)]
    [InlineData(0.5d)]
    [InlineData(1d)]
    public void Jitter_stays_within_half_of_the_base_delay(double jitter)
    {
        var delay = RetryPolicy.DelayFor(attempt: 1, jitter);

        Assert.InRange(delay.TotalSeconds, 2.5, 5.0);
    }

    [Fact]
    public void Jitter_is_clamped_to_a_valid_fraction()
    {
        Assert.InRange(RetryPolicy.DelayFor(1, jitter: -5d).TotalSeconds, 2.5, 5.0);
        Assert.InRange(RetryPolicy.DelayFor(1, jitter: 42d).TotalSeconds, 2.5, 5.0);
    }

    [Fact]
    public void Random_jitter_produces_a_usable_delay()
    {
        var delay = RetryPolicy.DelayFor(attempt: 2);

        Assert.InRange(delay.TotalSeconds, 5, 10);
    }

    [Fact]
    public void Negative_attempts_are_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RetryPolicy.DelayFor(-1));
}
