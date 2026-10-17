using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace DomainBlocks.EventStore.Subscriptions;

/// <summary>
/// Reads a store's event log forward from its end, waiting for events to be appended when it has read them all.
/// </summary>
/// <remarks>
/// <para>
/// After a full batch, the next read starts at once, since events may be left unread. After a short batch, it first
/// waits until the source's listener yields a later position, or for <see cref="EventLogTailOptions.PollInterval"/>, so
/// an append that is not heard costs at most one poll interval.
/// </para>
/// <para>
/// A transient error is retried with backoff from the same position, so no event is missed or read twice. Any other
/// error is thrown.
/// </para>
/// </remarks>
/// <param name="source">The source to read, which the cursor disposes.</param>
/// <param name="options">The batch size, poll interval and retry delays.</param>
/// <param name="logger">The logger for retries and for restarts of the listener.</param>
/// <param name="tailId">The ID of the tail session that reads through the cursor, which correlates its logs.</param>
internal sealed class EventLogCursor<TSourceEvent, TLogPos>(
    IEventLogCursorSource<TSourceEvent, TLogPos> source,
    EventLogTailOptions options,
    ILogger? logger,
    string tailId) :
    IAsyncDisposable
    where TLogPos : struct, IComparable<TLogPos>
{
    private readonly ResiliencePipeline _pipeline = CreatePipeline(source, options, logger, tailId);

    private readonly AppendWatcher<TLogPos> _appendWatcher =
        new(source.ListenForAppendsAsync, options, logger, tailId);

    private TLogPos? _position;
    private TLogPos? _waitAfter;
    private bool _waitForAppend;

    /// <summary>
    /// Reads the end of the log and moves there. The next <see cref="ReadNextAsync"/> reads the events appended after
    /// it, without waiting first.
    /// </summary>
    public async ValueTask SeekToEndAsync(CancellationToken cancellationToken)
    {
        // Listening starts first, so the appends just after the end are heard.
        _appendWatcher.Start();

        _position = await _pipeline
            .ExecuteAsync(static (source, ct) => source.ReadEndPositionAsync(ct), source, cancellationToken)
            .ConfigureAwait(false);

        _waitForAppend = false;
    }

    /// <summary>
    /// Reads the next batch of events, waiting until at least one has been appended, so the batch is never empty. The
    /// events are valid only until the next call.
    /// </summary>
    public async ValueTask<IReadOnlyList<TSourceEvent>> ReadNextAsync(CancellationToken cancellationToken)
    {
        while (true)
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

            var batch = await _pipeline
                .ExecuteAsync(
                    static (state, ct) => state.Source.ReadAsync(state.Position, state.MaxCount, ct),
                    (Source: source, Position: _position, MaxCount: options.MaxBatchSize),
                    cancellationToken)
                .ConfigureAwait(false);

            // A full batch may have left events unread, so only a short one waits before the next read.
            _waitForAppend = batch.Count < options.MaxBatchSize;

            if (batch.Count > 0)
                _position = source.GetLogPosition(batch[^1]);

            // The next wait ends at an append after both the read and what was heard before it. An append that was
            // heard but was not yet readable then waits for a later one, instead of being read for again and again.
            _waitAfter = AppendWatcher<TLogPos>.IsAfter(heardBeforeRead, _position) ? heardBeforeRead : _position;

            if (batch.Count > 0)
                return batch;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _appendWatcher.DisposeAsync().ConfigureAwait(false);
        await source.DisposeAsync().ConfigureAwait(false);
    }

    private static ResiliencePipeline CreatePipeline(
        IEventLogCursorSource<TSourceEvent, TLogPos> source,
        EventLogTailOptions options,
        ILogger? logger,
        string tailId)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = int.MaxValue,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.RetryDelay,
                MaxDelay = options.MaxRetryDelay,
                ShouldHandle = args =>
                {
                    var shouldHandle = args.Outcome.Exception is { } ex &&
                                       !args.Context.CancellationToken.IsCancellationRequested &&
                                       source.IsTransientError(ex);

                    return ValueTask.FromResult(shouldHandle);
                },
                OnRetry = args =>
                {
                    if (args.Outcome.Exception is { } ex)
                        logger?.TailRetrying(ex, tailId, args.AttemptNumber + 1, args.RetryDelay);

                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }
}