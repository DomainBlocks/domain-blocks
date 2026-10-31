using Microsoft.Extensions.Logging;
using Polly;

namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents the reads of one subscription's history, which recover from transient errors by themselves. It is the
/// counterpart to a feed: a subscription replays from it what the feed won't deliver.
/// </summary>
/// <param name="isTransient">
/// Whether the given exception is considered transient and the operation may be retried.
/// </param>
/// <param name="options">The subscription's options, which set how long a read keeps retrying.</param>
/// <param name="correlationId">The subscription's ID, which the log of each retry includes.</param>
/// <param name="logger">The logger for retries.</param>
internal abstract class CatchUpReader<TEvent, TPos, TMark>(
    Func<Exception, bool> isTransient,
    SubscriptionOptions options,
    string correlationId,
    ILogger? logger)
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    /// <summary>
    /// Gets the subscription's ID.
    /// </summary>
    protected string CorrelationId { get; } = correlationId;

    /// <summary>
    /// Gets the logger.
    /// </summary>
    protected ILogger? Logger { get; } = logger;

    /// <summary>
    /// Gets the resilience pipeline that retries each read. A store gives it to the catch-ups that it creates.
    /// </summary>
    protected ResiliencePipeline ResiliencePipeline { get; } = RetryPipeline.Create(
        isTransient,
        options.CatchUpMaxRetryAttempts,
        options.CatchUpRetryTimeout,
        (_, ex, attempt, delay) => logger?.CatchUpRetrying(ex, correlationId, attempt, delay));

    /// <summary>
    /// Reads the end of the followed sequence and returns the catch-up from the origin to that end, which covers every
    /// event up to the feed's high-water mark.
    /// </summary>
    public ValueTask<CatchUp<TEvent, TPos>> ReadAsync(
        SubscriptionOrigin<TPos> origin,
        TMark highWaterMark,
        CancellationToken cancellationToken)
    {
        return ResiliencePipeline.ExecuteAsync(
            static (s, ct) => s.Self.ReadCoreAsync(s.Origin, s.HighWaterMark, ct),
            (Self: this, Origin: origin, HighWaterMark: highWaterMark),
            cancellationToken);
    }

    /// <summary>
    /// Reads the end of the followed sequence, once events up to the high-water mark are visible to reads, and creates
    /// a catch-up from the origin to that end.
    /// </summary>
    protected abstract ValueTask<CatchUp<TEvent, TPos>> ReadCoreAsync(
        SubscriptionOrigin<TPos> origin,
        TMark highWaterMark,
        CancellationToken cancellationToken);
}