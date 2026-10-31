using Microsoft.Extensions.Logging;
using Polly;

namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents the reads of one subscription's history, which recover from transient errors by themselves. It is the
/// counterpart to a feed: a subscription replays from it what the feed won't deliver.
/// </summary>
/// <remarks>
/// A store derives from this class to supply its reads, and from <see cref="CatchUpSnapshot{TEvent,TPos}"/> to hold
/// what they need.
/// </remarks>
internal abstract class CatchUpSnapshotReader<TEvent, TPos, TMark>
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    /// <param name="isTransient">Whether an error is worth retrying.</param>
    /// <param name="options">The subscription's options, which set how long a read keeps retrying.</param>
    /// <param name="correlationId">The subscription's ID, which the log of each retry includes.</param>
    /// <param name="logger">The logger for retries.</param>
    protected CatchUpSnapshotReader(
        Func<Exception, bool> isTransient,
        SubscriptionOptions options,
        string correlationId,
        ILogger? logger)
    {
        CorrelationId = correlationId;
        Logger = logger;

        RetryPolicy = RetryPipeline.Create(
            isTransient,
            options.CatchUpMaxRetryAttempts,
            options.CatchUpRetryTimeout,
            (_, ex, attempt, delay) => logger?.CatchUpRetrying(ex, correlationId, attempt, delay));
    }

    /// <summary>
    /// Gets the subscription's ID.
    /// </summary>
    protected string CorrelationId { get; }

    /// <summary>
    /// Gets the logger.
    /// </summary>
    protected ILogger? Logger { get; }

    /// <summary>
    /// Gets the policy that retries each read. A store gives it to the snapshots that it creates.
    /// </summary>
    protected ResiliencePipeline RetryPolicy { get; }

    /// <summary>
    /// Reads a snapshot of the followed sequence from the origin, which covers every event up to the feed's high-water
    /// mark.
    /// </summary>
    public ValueTask<CatchUpSnapshot<TEvent, TPos>> ReadSnapshotAsync(
        SubscriptionOrigin<TPos> origin,
        TMark highWaterMark,
        CancellationToken cancellationToken)
    {
        return RetryPolicy.ExecuteAsync(
            static (s, ct) => s.Self.ReadSnapshotCoreAsync(s.Origin, s.HighWaterMark, ct),
            (Self: this, Origin: origin, HighWaterMark: highWaterMark),
            cancellationToken);
    }

    /// <summary>
    /// Reads the end of the followed sequence, once it can see the high-water mark, and creates a snapshot that ends
    /// there.
    /// </summary>
    protected abstract ValueTask<CatchUpSnapshot<TEvent, TPos>> ReadSnapshotCoreAsync(
        SubscriptionOrigin<TPos> origin,
        TMark highWaterMark,
        CancellationToken cancellationToken);
}