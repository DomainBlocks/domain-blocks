using DomainBlocks.EventStore.Filtering;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Reads a store's event log forward from its end, waiting for events to be appended when it has read them all.
/// </summary>
/// <remarks>
/// <para>
/// Each read runs to the end of the log, so the next read first waits until the source's listener yields a later
/// position, or for <see cref="EventLogTailOptions.PollInterval"/>, so an append that is not heard costs at most one
/// poll interval.
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
internal sealed class EventLogCursor<TEvent, TSourceEvent, TStreamId, TStreamPos, TLogPos>(
    IEventSubscriptionSource<TSourceEvent, TStreamId, TStreamPos, TLogPos> source,
    EventLogTailOptions options,
    ILogger? logger,
    string tailId) :
    IAsyncDisposable
    where TEvent : notnull
    where TSourceEvent : IReadEvent<TEvent, TStreamId, TStreamPos, TLogPos>
    where TStreamId : notnull
    where TStreamPos : struct
    where TLogPos : struct, IComparable<TLogPos>
{
    private readonly SubscriptionRetry _retry = new(
        source.IsTransientError,
        options,
        (ex, attempt, delay) => logger?.TailRetrying(ex, tailId, attempt, delay));

    private readonly AppendWatcher<TLogPos> _appendWatcher =
        new(source.ListenForAppendsAsync, options, logger, tailId);

    private TLogPos? _position;
    private TLogPos? _waitAfter;
    private bool _waitForAppend;

    /// <summary>
    /// Reads the end of the log and moves there. The next <see cref="ReadAsync"/> reads the events appended after it,
    /// without waiting first.
    /// </summary>
    public async ValueTask SeekToEndAsync(CancellationToken cancellationToken)
    {
        // Listening starts first, so the appends just after the end are heard.
        _appendWatcher.Start();

        _position = await _retry.Pipeline
            .ExecuteAsync(static (source, ct) => source.ReadEndPositionAsync(ct), source, cancellationToken)
            .ConfigureAwait(false);

        _retry.OnProgress();
        _waitForAppend = false;
    }

    /// <summary>
    /// Reads the events appended since the last read, to the end of the log, and hands each to
    /// <paramref name="onEvent"/>, which must not throw. Unless this is the first read since
    /// <see cref="SeekToEndAsync"/>, it first waits until an append is heard, or for the poll interval. Each event is
    /// valid only during its call to <paramref name="onEvent"/>.
    /// </summary>
    public async ValueTask ReadAsync(Action<TSourceEvent> onEvent, CancellationToken cancellationToken)
    {
        // If the wait times out, the cursor reads anyway in case the append was not heard.
        if (_waitForAppend)
        {
            await _appendWatcher
                .WaitAsync(_waitAfter, options.PollInterval, cancellationToken)
                .ConfigureAwait(false);
        }

        // Taken before the read, so an append heard during the read ends the next wait at once.
        var heardBeforeRead = _appendWatcher.HighWaterMark;

        await _retry.Pipeline
            .ExecuteAsync(
                static (state, ct) => state.Cursor.ReadToEndAsync(state.OnEvent, ct),
                (Cursor: this, OnEvent: onEvent),
                cancellationToken)
            .ConfigureAwait(false);

        // The read ran to the end of the log, so the next one waits. That wait ends at an append after both the read
        // and what was heard before it, so an append that was heard but was not yet readable waits for a later one,
        // instead of being read for again and again.
        _waitForAppend = true;
        _waitAfter = AppendWatcher<TLogPos>.IsAfter(heardBeforeRead, _position) ? heardBeforeRead : _position;
    }

    public async ValueTask DisposeAsync()
    {
        await _appendWatcher.DisposeAsync().ConfigureAwait(false);
        await source.DisposeAsync().ConfigureAwait(false);
    }

    // The position moves past each event once it has been handed on, so a retry resumes after the last one.
    private async ValueTask ReadToEndAsync(Action<TSourceEvent> onEvent, CancellationToken cancellationToken)
    {
        var events = source.ReadAllAsync(_position, toInclusive: null, EventFilter.All, cancellationToken);

        await foreach (var e in events.ConfigureAwait(false))
        {
            onEvent(e);
            _position = e.Context.LogPosition;
            _retry.OnProgress();
        }

        _retry.OnProgress();
    }
}