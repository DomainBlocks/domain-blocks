using DomainBlocks.EventStore.Filtering;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Reads a store's event log forward from its end, and waits for events to be appended once it has read them all.
/// </summary>
/// <remarks>
/// <para>
/// Each read runs to the end of the log, so the caller then waits until the source's listener yields a later position,
/// or for <see cref="EventLogTailOptions.PollInterval"/>, so an append that is not heard costs at most one poll
/// interval.
/// </para>
/// <para>
/// A transient error is retried with backoff from the last event that was handed on, so no event is missed or handed
/// on twice. Any other error is thrown, as is a transient one once the cursor has gone
/// <see cref="EventLogTailOptions.RetryTimeout"/> without handing on an event or completing a read.
/// </para>
/// </remarks>
/// <param name="source">The source to read, which the cursor disposes.</param>
/// <param name="options">The poll interval and retry delays.</param>
/// <param name="logger">The logger for retries and for restarts of the listener.</param>
/// <param name="tailId">The ID of the tail session that reads through the cursor, which correlates its logs.</param>
internal sealed class EventLogCursor<TSourceEvent, TResultEvent, TStreamId, TStreamPos, TLogPos>(
    IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos> source,
    EventLogTailOptions options,
    ILogger? logger,
    string tailId) :
    IAsyncDisposable
    where TSourceEvent : IReadEvent<TResultEvent, TStreamId, TStreamPos, TLogPos>
    where TResultEvent : notnull
    where TStreamId : notnull
    where TStreamPos : struct
    where TLogPos : struct, IComparable<TLogPos>
{
    private readonly SubscriptionRetry _retry = new(
        source.IsTransientError,
        options,
        (ex, attempt, delay) => logger?.TailRetrying(ex, tailId, attempt, delay));

    private readonly AppendSignal<TLogPos> _appendSignal =
        new(source.ListenForAppendsAsync, options, logger, tailId);

    private TLogPos? _readPosition;
    private TLogPos? _highWaterMarkBeforeRead;

    /// <summary>
    /// Reads the end of the log and moves there. The next <see cref="ReadAsync"/> reads the events appended after it.
    /// </summary>
    public async ValueTask SeekToEndAsync(CancellationToken cancellationToken)
    {
        // Listening starts first, so the appends just after the end are heard.
        _appendSignal.Start();

        _readPosition = await _retry.Pipeline
            .ExecuteAsync(static (source, ct) => source.ReadEndLogPositionAsync(ct), source, cancellationToken)
            .ConfigureAwait(false);

        _retry.OnProgress();
    }

    /// <summary>
    /// Reads the events appended since the last read, to the end of the log, and hands each to
    /// <paramref name="onEvent"/>, which must not throw. Each event is valid only during its call to
    /// <paramref name="onEvent"/>.
    /// </summary>
    public async ValueTask ReadAsync(Action<TSourceEvent> onEvent, CancellationToken cancellationToken)
    {
        // Taken before the read, so that the next wait can tell an append heard during the read from one heard before.
        _highWaterMarkBeforeRead = _appendSignal.HighWaterMark;

        await _retry.Pipeline
            .ExecuteAsync(
                static (state, ct) => state.Cursor.ReadToEndAsync(state.OnEvent, ct),
                (Cursor: this, OnEvent: onEvent),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until an append after the last read is heard, or for the poll interval. If the wait times out, the caller
    /// reads anyway, in case the append was not heard.
    /// </summary>
    public async ValueTask WaitForAppendAsync(CancellationToken cancellationToken)
    {
        // The wait ends at an append after both the last read and what was heard before it. So an append heard during
        // the read ends it at once, because the read may have missed that append. But an append that was heard before
        // the read, and that the read did not return, was not yet readable, so the wait ends at a later one, instead of
        // the cursor reading for it again and again.
        var after = AppendSignal<TLogPos>.IsAfter(_highWaterMarkBeforeRead, _readPosition)
            ? _highWaterMarkBeforeRead
            : _readPosition;

        await _appendSignal.WaitAsync(after, options.PollInterval, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _appendSignal.DisposeAsync().ConfigureAwait(false);
        await source.DisposeAsync().ConfigureAwait(false);
    }

    // The position moves past each event once it has been handed on, so a retry resumes after the last one.
    private async ValueTask ReadToEndAsync(Action<TSourceEvent> onEvent, CancellationToken cancellationToken)
    {
        var events = source.ReadAllAsync(_readPosition, toInclusive: null, EventFilter.All, cancellationToken);

        await foreach (var e in events.ConfigureAwait(false))
        {
            onEvent(e);
            _readPosition = e.Context.LogPosition;
            _retry.OnProgress();
        }

        _retry.OnProgress();
    }
}