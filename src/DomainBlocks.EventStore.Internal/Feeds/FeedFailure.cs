namespace DomainBlocks.EventStore.Internal.Feeds;

/// <summary>
/// Represents a transient failure that a feed will retry: a session that it couldn't create, or one that it lost.
/// </summary>
/// <remarks>
/// Failures belong to one outage until a session has stayed up for <see cref="FeedResumeOptions.StabilityPeriod"/>, and
/// the failure that ends such a session starts a new outage.
/// </remarks>
/// <param name="error">The error.</param>
/// <param name="failureCount">The number of failures in the outage so far, including this one.</param>
/// <param name="outageDuration">The time since the outage's first failure.</param>
/// <param name="hasConnected">Whether the feed has connected before this failure.</param>
internal sealed class FeedFailure(Exception error, int failureCount, TimeSpan outageDuration, bool hasConnected)
{
    /// <summary>
    /// Gets the error.
    /// </summary>
    public Exception Error { get; } = error;

    /// <summary>
    /// Gets the number of failures in the outage so far, including this one.
    /// </summary>
    public int FailureCount { get; } = failureCount;

    /// <summary>
    /// Gets the time since the outage's first failure.
    /// </summary>
    public TimeSpan OutageDuration { get; } = outageDuration;

    /// <summary>
    /// Gets a value that indicates whether the feed has connected before this failure.
    /// </summary>
    public bool HasConnected { get; } = hasConnected;
}