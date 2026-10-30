using Microsoft.Extensions.Logging;
using Polly;

namespace DomainBlocks.EventStore.Internal.Subscriptions;

/// <summary>
/// Represents a subscription's reads of a store's history, which recover from transient errors by themselves.
/// </summary>
internal sealed class CatchUpReader<TEvent, TMark, TPos>
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    private readonly IHistoryReader<TEvent, TMark> _reader;
    private readonly Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> _positionSelector;
    private readonly string _correlationId;
    private readonly ResiliencePipeline _retryPipeline;

    /// <param name="reader">The store's reader of the followed sequence's history.</param>
    /// <param name="positionSelector">Selects an event's position in the followed sequence.</param>
    /// <param name="options">The subscription's options, which set how long a read keeps retrying.</param>
    /// <param name="isTransient">Whether an error is worth retrying.</param>
    /// <param name="correlationId">The subscription's ID, which the log of each retry includes.</param>
    /// <param name="logger">The logger for retries.</param>
    public CatchUpReader(
        IHistoryReader<TEvent, TMark> reader,
        Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> positionSelector,
        SubscriptionOptions options,
        Func<Exception, bool> isTransient,
        string correlationId,
        ILogger? logger)
    {
        _reader = reader;
        _positionSelector = positionSelector;
        _correlationId = correlationId;

        _retryPipeline = RetryPipeline.Create(
            isTransient,
            options.CatchUpMaxRetryAttempts,
            options.CatchUpRetryTimeout,
            (_, ex, attempt, delay) => logger?.CatchUpRetrying(ex, correlationId, attempt, delay));
    }

    /// <summary>
    /// Reads a snapshot of the followed sequence from the origin, which covers every event up to the feed's high-water
    /// mark.
    /// </summary>
    public async ValueTask<CatchUpSnapshot<TEvent, TPos>> ReadSnapshotAsync(
        TMark highWaterMark,
        SubscriptionOrigin<TPos> origin,
        CancellationToken cancellationToken)
    {
        var snapshot = await _retryPipeline
            .ExecuteAsync(
                static (s, ct) => s.Reader.ReadSnapshotAsync(s.Mark, s.CorrelationId, ct),
                (Reader: _reader, Mark: highWaterMark, CorrelationId: _correlationId),
                cancellationToken)
            .ConfigureAwait(false);

        return new CatchUpSnapshot<TEvent, TPos>(snapshot, origin, _positionSelector, _retryPipeline);
    }
}