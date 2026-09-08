using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.PostgreSQL.Feeds;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.PostgreSQL.Subscriptions;

/// <summary>
/// A catch-up-then-live subscription, ported from the MongoDB implementation.
/// </summary>
/// <remarks>
/// <para>
/// Each cycle attaches an observer to the live feed first, then reads the high-water mark (the highest committed
/// position), replays everything from the resume position up to it, emits <see cref="SubscriptionMessage.CaughtUp"/>,
/// and finally drains the live rows, skipping any at or below the high-water mark. Because positions are assigned in
/// commit order without gaps, the replay and the live rows tile exactly: nothing is skipped and nothing is repeated.
/// </para>
/// <para>
/// A cycle is restarted from the last delivered position, after emitting
/// <see cref="SubscriptionMessage.FellBehind"/>, when the subscriber's queue overflows or when the feed has been
/// re-established and may have missed rows.
/// </para>
/// </remarks>
internal sealed class SubscriptionAsyncEnumerable<TEvent, TPos> : IAsyncEnumerable<SubscriptionMessage>
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    private readonly SubscriptionOrigin<TPos> _origin;
    private readonly SubscriptionOptions _options;
    private readonly SubscriptionTarget<TPos> _target;
    private readonly EventLogReader _reader;
    private readonly IRefCountedEventLogFeed _feed;
    private readonly IEventDecoder<TEvent, PostgresEventData, string> _decoder;
    private readonly ILogger? _logger;
    private readonly string _correlationId;

    public SubscriptionAsyncEnumerable(
        SubscriptionOrigin<TPos>? origin,
        SubscriptionOptions? options,
        SubscriptionTarget<TPos> target,
        EventLogReader reader,
        IRefCountedEventLogFeed feed,
        IEventDecoder<TEvent, PostgresEventData, string> decoder,
        ILogger? logger)
    {
        _origin = origin ?? SubscriptionOrigin.End<TPos>();
        _options = options ?? SubscriptionOptions.Default;
        _target = target;
        _reader = reader;
        _feed = feed;
        _decoder = decoder;
        _logger = logger;

        _correlationId = _options.CorrelationId is { } id
            ? CorrelationId.Reserve(id)
            : CorrelationId.ReserveGenerated();
    }

    public async IAsyncEnumerator<SubscriptionMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        Observer? observer = null;
        IAsyncDisposable? attachment = null;

        try
        {
            _logger?.SubscriptionStarted(_correlationId);

            var resumeOrigin = _origin;
            var fellBehindPending = false;

            while (true)
            {
                // Attach the new observer before detaching the old one, so that the ref count never drops to zero
                // between cycles and the feed's replication session survives a restart.
                var nextObserver = new Observer(_options.QueueCapacity);
                var nextAttachment = await AttachObserverAsync(nextObserver, cancellationToken).ConfigureAwait(false);

                if (attachment is not null)
                    await attachment.DisposeAsync().ConfigureAwait(false);

                observer?.Dispose();
                observer = nextObserver;
                attachment = nextAttachment;

                var enumerator = ReadAllAsync(resumeOrigin, observer, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                await using (enumerator.ConfigureAwait(false))
                {
                    while (await MoveNextAsync(enumerator, observer.RestartToken, cancellationToken)
                               .ConfigureAwait(false))
                    {
                        var message = enumerator.Current;
                        yield return message;

                        if (message is SubscriptionMessage.CaughtUp)
                            fellBehindPending = false;
                        else if (TryGetContext(message, out var context))
                            resumeOrigin = SubscriptionOrigin.After(_target.PositionOf(context));
                    }
                }

                switch (observer.RestartReason)
                {
                    case RestartReason.QueueOverflow:
                        _logger?.SubscriptionFellBehind(_correlationId);
                        break;

                    case RestartReason.FeedReset:
                        _logger?.SubscriptionFeedReset(_correlationId);
                        break;

                    default:
                        yield break;
                }

                // A cycle can restart again before it has caught up, e.g. when the live feed is still pumping a large
                // transaction into a small queue. Every FellBehind is answered by exactly one CaughtUp, so a restart
                // that has not yet been caught up on is not reported twice.
                if (!fellBehindPending)
                {
                    fellBehindPending = true;
                    yield return new SubscriptionMessage.FellBehind();
                }
            }
        }
        finally
        {
            if (attachment is not null)
                await attachment.DisposeAsync().ConfigureAwait(false);

            observer?.Dispose();

            _logger?.SubscriptionStopped(_correlationId);
            CorrelationId.Release(_correlationId);
        }
    }

    private static bool TryGetContext(
        SubscriptionMessage message,
        out ReadEventContext<string, StreamPosition, LogPosition> context)
    {
        if (message is SubscriptionMessage.Event<ReadEvent<TEvent, string, StreamPosition, LogPosition>> e)
        {
            context = e.Value.Context;
            return true;
        }

        context = default;
        return false;
    }

    private async ValueTask<bool> MoveNextAsync(
        IAsyncEnumerator<SubscriptionMessage> enumerator,
        CancellationToken restartToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await enumerator.MoveNextAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (restartToken.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.SubscriptionCanceled(_correlationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger?.SubscriptionFailed(ex, _correlationId);
            throw;
        }
    }

    private async Task<IAsyncDisposable> AttachObserverAsync(Observer observer, CancellationToken cancellationToken)
    {
        try
        {
            return await _feed.AttachAsync(observer, _correlationId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            observer.Dispose();
            _logger?.SubscriptionCanceled(_correlationId);
            throw;
        }
        catch (Exception ex)
        {
            observer.Dispose();
            _logger?.SubscriptionFailed(ex, _correlationId);
            throw;
        }
    }

    private async IAsyncEnumerable<SubscriptionMessage> ReadAllAsync(
        SubscriptionOrigin<TPos> resumeOrigin,
        Observer observer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long? highWaterMark;

        using (var catchUpCts = CancellationTokenSource.CreateLinkedTokenSource(
                   cancellationToken,
                   observer.RestartToken))
        {
            highWaterMark = await _reader.GetMaxPositionAsync(catchUpCts.Token).ConfigureAwait(false);

            _logger?.CatchUpBoundary(_correlationId, highWaterMark);

            if (highWaterMark is not null && resumeOrigin is not SubscriptionOrigin<TPos>.End)
            {
                var afterExclusive = resumeOrigin is SubscriptionOrigin<TPos>.After after
                    ? checked((long)after.Position.Value)
                    : -1;

                var rows = _target.ReadCatchUpAsync(_reader, afterExclusive, highWaterMark.Value, catchUpCts.Token);

                await foreach (var row in rows.ConfigureAwait(false))
                    yield return SubscriptionMessage.Event.Create(_decoder.Decode(row));
            }
        }

        _logger?.SubscriptionCaughtUp(_correlationId);

        yield return new SubscriptionMessage.CaughtUp();

        await foreach (var row in observer.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (row.Position <= highWaterMark || !_target.IsLiveMatch(row))
                continue;

            yield return SubscriptionMessage.Event.Create(_decoder.Decode(row));
        }
    }

    private enum RestartReason
    {
        None,
        QueueOverflow,
        FeedReset,
        Disposed
    }

    /// <summary>
    /// Buffers live rows for one subscription cycle. Never blocks the feed: an overflow, like a feed reset, cancels the
    /// restart token and completes the channel so that the cycle ends and a new one starts from the last position.
    /// </summary>
    private sealed class Observer(int queueCapacity) : IEventLogObserver, IDisposable
    {
        private readonly Channel<EventLogRow> _channel = Channel.CreateBounded<EventLogRow>(
            new BoundedChannelOptions(queueCapacity)
            {
                SingleWriter = true,
                SingleReader = true
            });

        private readonly CancellationTokenSource _restartCts = new();
        private int _restartReason;

        public ChannelReader<EventLogRow> Reader => _channel.Reader;

        public CancellationToken RestartToken => _restartCts.Token;

        public RestartReason RestartReason => (RestartReason)Volatile.Read(ref _restartReason);

        public ValueTask OnNextAsync(EventLogRow row, CancellationToken cancellationToken)
        {
            if (!_channel.Writer.TryWrite(row))
                Restart(RestartReason.QueueOverflow);

            return ValueTask.CompletedTask;
        }

        public ValueTask OnResetAsync(CancellationToken cancellationToken)
        {
            Restart(RestartReason.FeedReset);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            _channel.Writer.TryComplete(exception);
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _restartReason, (int)RestartReason.Disposed, 0) == 0)
                _channel.Writer.TryComplete();

            _restartCts.Dispose();
        }

        private void Restart(RestartReason reason)
        {
            if (Interlocked.CompareExchange(ref _restartReason, (int)reason, 0) != 0)
                return;

            _restartCts.Cancel();
            _channel.Writer.TryComplete(new OperationCanceledException(_restartCts.Token));
        }
    }
}
