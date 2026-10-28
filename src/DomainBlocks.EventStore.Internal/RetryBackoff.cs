namespace DomainBlocks.EventStore.Internal;

/// <summary>
/// Paces the retries of reads that fail with transient errors, and tells when to stop retrying. Each delay is chosen at
/// random between half of a ceiling and all of it. The ceiling starts at the first delay and doubles after each retry,
/// up to the maximum delay. Once the timeout has passed since the first failure after the last progress, no more
/// retries are allowed.
/// </summary>
internal sealed class RetryBackoff(TimeSpan timeout, TimeSpan firstDelay, TimeSpan maxDelay, TimeProvider timeProvider)
{
    /// <summary>
    /// The default ceiling of the first delay, which is 1 second.
    /// </summary>
    public static readonly TimeSpan DefaultFirstDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The default maximum delay, which is 1 minute.
    /// </summary>
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromMinutes(1);

    private readonly TimeSpan _firstDelay = firstDelay;
    private TimeSpan _ceiling = firstDelay;
    private long? _failingSince;

    public RetryBackoff(TimeSpan timeout) : this(timeout, DefaultFirstDelay, DefaultMaxDelay, TimeProvider.System)
    {
    }

    /// <summary>
    /// Records that the reads made progress, so the next failure starts again from the first delay, and the timeout
    /// runs from that failure.
    /// </summary>
    public void Reset()
    {
        _ceiling = _firstDelay;
        _failingSince = null;
    }

    /// <summary>
    /// Records a failure, and gets the delay before the next retry. Returns <see langword="false"/> once the timeout
    /// has passed since the first failure after the last progress.
    /// </summary>
    public bool TryGetNextDelay(out TimeSpan delay)
    {
        var now = timeProvider.GetTimestamp();
        _failingSince ??= now;

        if (timeout != Timeout.InfiniteTimeSpan && timeProvider.GetElapsedTime(_failingSince.Value, now) >= timeout)
        {
            delay = TimeSpan.Zero;
            return false;
        }

        delay = _ceiling * (0.5 + (Random.Shared.NextDouble() / 2));
        _ceiling = TimeSpan.FromTicks(Math.Min(_ceiling.Ticks * 2, maxDelay.Ticks));

        return true;
    }
}