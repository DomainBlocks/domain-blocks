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
/// waits until an event is appended, or for <see cref="EventLogTailOptions.PollInterval"/>, so a missed append costs at
/// most one poll interval.
/// </para>
/// <para>
/// A transient error is retried with backoff from the same position, so no event is missed or read twice. Any other
/// error is thrown.
/// </para>
/// </remarks>
/// <param name="source">The source to read, which the cursor disposes.</param>
/// <param name="options">The batch size, poll interval and retry delays.</param>
/// <param name="logger">The logger for retries.</param>
/// <param name="tailId">The ID of the tail session that reads through the cursor, which correlates its retries.</param>
internal sealed class EventLogCursor<TSourceEvent, TLogPos>(
    IEventLogCursorSource<TSourceEvent, TLogPos> source,
    EventLogTailOptions options,
    ILogger? logger,
    string tailId) :
    IAsyncDisposable
    where TLogPos : struct
{
    private readonly ResiliencePipeline _pipeline = CreatePipeline(source, options, logger, tailId);
    private TLogPos? _position;
    private bool _waitForAppend;

    /// <summary>
    /// Reads the end of the log and moves there. The next <see cref="ReadNextAsync"/> reads the events appended after
    /// it, without waiting first.
    /// </summary>
    public async ValueTask SeekToEndAsync(CancellationToken cancellationToken)
    {
        _position = await _pipeline
            .ExecuteAsync(static (source, ct) => source.ReadEndAsync(ct), source, cancellationToken)
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
            // If the wait times out, the cursor reads anyway in case the append was missed.
            if (_waitForAppend)
                await source.WaitForAppendAsync(options.PollInterval, cancellationToken).ConfigureAwait(false);

            var batch = await _pipeline
                .ExecuteAsync(
                    static (state, ct) => state.Source.ReadAfterAsync(state.Position, state.MaxCount, ct),
                    (Source: source, Position: _position, MaxCount: options.MaxBatchSize),
                    cancellationToken)
                .ConfigureAwait(false);

            // A full batch may have left events unread, so only a short one waits before the next read.
            _waitForAppend = batch.Count < options.MaxBatchSize;

            if (batch.Count > 0)
            {
                _position = source.GetLogPosition(batch[^1]);
                return batch;
            }
        }
    }

    public ValueTask DisposeAsync() => source.DisposeAsync();

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
                                       source.IsTransient(ex);

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