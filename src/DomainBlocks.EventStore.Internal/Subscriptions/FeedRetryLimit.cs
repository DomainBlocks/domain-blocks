using DomainBlocks.EventStore.Internal.Feeds;

namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents how long a subscription waits for its feed to recover. The limits run from the first failure in an outage
/// that this class is given, so a subscription that joins an outage late still gets all of them.
/// </summary>
/// <param name="maxRetryAttempts">How many retries by the feed to wait for after that failure.</param>
/// <param name="timeout">
/// How long to wait after that failure, or <see cref="Timeout.InfiniteTimeSpan"/> to wait without a limit.
/// </param>
internal sealed class FeedRetryLimit(int maxRetryAttempts, TimeSpan timeout)
{
    private FeedFailure? _firstFailure;

    /// <summary>
    /// Determines whether a failure reaches either limit. Call this for each failure in turn.
    /// </summary>
    public bool IsReachedBy(FeedFailure failure)
    {
        if (_firstFailure is null || failure.FailureCount == 1)
            _firstFailure = failure;

        return failure.FailureCount - _firstFailure.FailureCount >= maxRetryAttempts ||
               timeout != Timeout.InfiniteTimeSpan &&
               failure.OutageDuration - _firstFailure.OutageDuration >= timeout;
    }
}