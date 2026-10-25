using Workspace.Application.Abstractions;

namespace Workspace.UnitTests;

/// <summary>
/// A clock the test controls, so time-dependent behaviour is asserted rather
/// than waited for.
/// </summary>
public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public static readonly DateTimeOffset Default = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; private set; } = now;

    public FixedClock() : this(Default)
    {
    }

    public void Advance(TimeSpan amount) => UtcNow = UtcNow.Add(amount);
}
