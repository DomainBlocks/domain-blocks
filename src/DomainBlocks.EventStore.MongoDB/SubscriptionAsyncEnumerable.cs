using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

internal class SubscriptionAsyncEnumerable<TEvent, TPos> :
    IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>>
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    private readonly IMongoCollection<BsonDocument> _eventLog;
    private readonly RefCountedChangeStreamSubject<EventLogDocument<TEvent>> _changeStreamSubject;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _eventDecoder;
    private readonly FilterDefinition<BsonDocument>? _catchUpFilter;
    private readonly EventFilter _catchUpRemainder;
    private readonly string? _streamId;
    private readonly EventFilter _liveFilter;
    private readonly string _positionFieldName;
    private readonly Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> _positionSelector;
    private readonly SubscriptionOrigin<TPos> _origin;
    private readonly SubscriptionOptions _options;
    private readonly ILogger? _logger;
    private readonly string _correlationId;

    public SubscriptionAsyncEnumerable(
        IMongoCollection<BsonDocument> eventLog,
        RefCountedChangeStreamSubject<EventLogDocument<TEvent>> changeStreamSubject,
        IEventDecoder<TEvent, BsonValue, BsonValue> eventDecoder,
        FilterDefinition<BsonDocument>? catchUpFilter,
        EventFilter catchUpRemainder,
        string? streamId,
        EventFilter liveFilter,
        string positionFieldName,
        Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> positionSelector,
        SubscriptionOrigin<TPos>? origin,
        SubscriptionOptions? options,
        ILogger? logger)
    {
        _origin = origin ?? SubscriptionOrigin.End;
        _options = options ?? SubscriptionOptions.Default;
        _catchUpFilter = catchUpFilter;
        _catchUpRemainder = catchUpRemainder;
        _streamId = streamId;
        _liveFilter = liveFilter;
        _positionFieldName = positionFieldName;
        _positionSelector = positionSelector;
        _eventLog = eventLog;
        _changeStreamSubject = changeStreamSubject;
        _eventDecoder = eventDecoder;
        _logger = logger;

        _correlationId = _options.CorrelationId is { } id
            ? CorrelationId.Reserve(id)
            : CorrelationId.ReserveGenerated();
    }

    public async IAsyncEnumerator<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.SubscriptionStarted(_correlationId);

            var resumeOrigin = _origin;

            while (true)
            {
                using var observer = new Observer(
                    _options.QueueCapacity,
                    _streamId,
                    _liveFilter,
                    _options.CheckpointInterval);

                await using var attachment = await AttachObserver(observer).ConfigureAwait(false);

                var enumerator = ReadAllAsync(resumeOrigin, observer, attachment.OperationTime, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                await using (enumerator.ConfigureAwait(false))
                {
                    while (await MoveNextAsync(enumerator, observer.OverflowToken, cancellationToken)
                               .ConfigureAwait(false))
                    {
                        var m = enumerator.Current;
                        yield return m;

                        if (m.Event is { } e)
                            resumeOrigin = SubscriptionOrigin.After(_positionSelector(e.Context));
                        else if (CheckpointOf(m) is { } checkpoint)
                            resumeOrigin = SubscriptionOrigin.After(checkpoint);
                    }
                }

                if (observer.OverflowToken.IsCancellationRequested)
                {
                    _logger?.SubscriptionFellBehind(_correlationId);
                    yield return SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>.FellBehind;
                    continue;
                }

                yield break;
            }
        }
        finally
        {
            _logger?.SubscriptionStopped(_correlationId);
            CorrelationId.Release(_correlationId);
        }
    }

    private async ValueTask<bool> MoveNextAsync(
        IAsyncEnumerator<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> enumerator,
        CancellationToken overflowToken,
        CancellationToken cancellationToken)
    {
        try
        {
            return await enumerator.MoveNextAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (overflowToken.IsCancellationRequested)
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

    private async Task<IChangeStreamAttachment> AttachObserver(Observer observer)
    {
        try
        {
            return await _changeStreamSubject.AttachAsync(observer, _correlationId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.SubscriptionFailed(ex, _correlationId);
            throw;
        }
    }

    private async IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> ReadAllAsync(
        SubscriptionOrigin<TPos> resumeOrigin,
        Observer observer,
        BsonTimestamp changeStreamOperationTime,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LogPosition? highWaterMark;

        // Where the subscriber has got to: what it asked to resume after, then the last event or checkpoint it is given.
        TPos? last = resumeOrigin is SubscriptionOrigin<TPos>.After resumedAfter ? resumedAfter.Position : null;

        using (var catchUpCts = CancellationTokenSource.CreateLinkedTokenSource(
                   cancellationToken,
                   observer.OverflowToken))
        {
            // The majority read waits until everything up to the change stream's anchor is committed, so nothing
            // falls between catch-up and live. See docs/unpublished/event-store-database-contract.md.
            using var session = await StartCatchUpSessionAsync(changeStreamOperationTime, catchUpCts.Token)
                .ConfigureAwait(false);

            highWaterMark = await GetHighWaterMarkAsync(session, catchUpCts.Token).ConfigureAwait(false);

            _logger?.CatchUpBoundary(_correlationId, highWaterMark?.Value);

            if (highWaterMark is not null)
            {
                // Its own, as the store's is set by the change stream while this catches up.
                var document = new EventLogDocument<TEvent>(_eventDecoder, includeMetadata: true);

                await foreach (var doc in ReadCatchUpAsync(session, resumeOrigin, highWaterMark.Value, catchUpCts.Token)
                                   .ConfigureAwait(false))
                {
                    document.Set(doc);

                    // What the database could not evaluate of the filter.
                    if (!_catchUpRemainder.Matches(document))
                        continue;

                    var e = document.DecodedEvent;
                    last = _positionSelector(e.Context);
                    yield return SubscriptionMessage.Event(e);
                }

                // With a filter, catching up may have looked past the last event it delivered, or past where the
                // subscriber was if it delivered none. A subscription without one is given every event, and has no need.
                if (_liveFilter is not AllEventsFilter)
                {
                    var checkpoint = await GetCatchUpCheckpointAsync(session, highWaterMark.Value, catchUpCts.Token)
                        .ConfigureAwait(false);

                    if (checkpoint is { } position && (last is null || position.Value > last.Value.Value))
                    {
                        last = position;
                        yield return ToCheckpoint(position);
                    }
                }
            }
        }

        _logger?.SubscriptionCaughtUp(_correlationId);

        yield return SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>.CaughtUp;

        await foreach (var message in observer.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (message.Event is { } e)
            {
                // Catching up has delivered it, if it was to be.
                if (e.Context.LogPosition.Value <= highWaterMark?.Value)
                    continue;

                last = _positionSelector(e.Context);
            }
            else if (CheckpointOf(message) is { } checkpoint)
            {
                // A change from before the subscription caught up says nothing new, and must not take a subscriber back.
                if (last is not null && checkpoint.Value <= last.Value.Value)
                    continue;

                last = checkpoint;
            }

            yield return message;
        }
    }

    // How far catching up has looked, in the position that the subscription resumes by: the high-water mark of the log,
    // or how far the stream had got by then, which is null if it had no events.
    private async Task<TPos?> GetCatchUpCheckpointAsync(
        IClientSessionHandle session,
        LogPosition highWaterMark,
        CancellationToken cancellationToken)
    {
        if (_streamId is null)
            return (TPos)(object)highWaterMark;

        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, _streamId) &
                     Builders<BsonDocument>.Filter.Lte(EventLogEntry.FieldNames.Position, highWaterMark.Value);

        var result = await _eventLog
            .Find(session, filter)
            .Sort(Builders<BsonDocument>.Sort.Descending(EventLogEntry.FieldNames.StreamPosition))
            .Limit(1)
            .Project(Builders<BsonDocument>.Projection.Include(EventLogEntry.FieldNames.StreamPosition))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return result?[EventLogEntry.FieldNames.StreamPosition].AsInt64 is { } value
            ? (TPos)(object)StreamPosition.FromInt64(value)
            : null;
    }

    private static TPos? CheckpointOf(SubscriptionMessage<TEvent, string, StreamPosition, LogPosition> message)
    {
        return message switch
        {
            { LogCheckpoint: { HasValue: true, Value: TPos position } } => position,
            { StreamCheckpoint: { HasValue: true, Value: TPos position } } => position,
            _ => null
        };
    }

    private static SubscriptionMessage<TEvent, string, StreamPosition, LogPosition> ToCheckpoint(TPos position)
    {
        return position switch
        {
            LogPosition p => SubscriptionMessage.LogCheckpoint<TEvent, string, StreamPosition, LogPosition>(p),
            StreamPosition p => SubscriptionMessage.StreamCheckpoint<TEvent, string, StreamPosition, LogPosition>(p),
            _ => throw new UnreachableException($"Unexpected position type '{typeof(TPos).Name}'.")
        };
    }

    private async Task<IClientSessionHandle> StartCatchUpSessionAsync(
        BsonTimestamp changeStreamOperationTime,
        CancellationToken cancellationToken)
    {
        var session = await _eventLog.Database.Client
            .StartSessionAsync(new ClientSessionOptions { CausalConsistency = true }, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            session.AdvanceOperationTime(changeStreamOperationTime);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private async Task<LogPosition?> GetHighWaterMarkAsync(
        IClientSessionHandle session,
        CancellationToken cancellationToken)
    {
        var projection = Builders<BsonDocument>.Projection.Include(EventLogEntry.FieldNames.Position);

        var result = await _eventLog
            .Find(session, Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending(EventLogEntry.FieldNames.Position))
            .Limit(1)
            .Project(projection)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return result?[EventLogEntry.FieldNames.Position].AsInt64 is { } value ? LogPosition.FromInt64(value) : null;
    }

    private async IAsyncEnumerable<BsonDocument> ReadCatchUpAsync(
        IClientSessionHandle session,
        SubscriptionOrigin<TPos> resumeOrigin,
        LogPosition highWaterMark,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // From the end there is nothing to catch up on, and a filter may leave nothing to ask the database for.
        if (resumeOrigin is SubscriptionOrigin<TPos>.End || _catchUpFilter is not { } filter)
            yield break;

        if (resumeOrigin is SubscriptionOrigin<TPos>.After after)
            filter &= Builders<BsonDocument>.Filter.Gt(_positionFieldName, after.Position.Value);

        filter &= Builders<BsonDocument>.Filter.Lte(EventLogEntry.FieldNames.Position, highWaterMark.Value);

        // Same session, so the snapshot includes the high-water mark.
        using var cursor = await _eventLog
            .Find(session, filter)
            .Sort(Builders<BsonDocument>.Sort.Ascending(EventLogEntry.FieldNames.Position))
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var doc in cursor.Current)
                yield return doc;
        }
    }

    /// <summary>
    /// Buffers live events for one subscription. Never blocks the change stream: an overflow cancels the overflow
    /// token and completes the channel, so that the subscription starts again from its last position.
    /// </summary>
    /// <remarks>
    /// With a filter, it also says how far it has looked. The change stream notes the position of each change that
    /// the filter passes over, and a timer queues the last of them as a checkpoint. The change stream queues an event
    /// before it notes any later position, so a checkpoint is in the queue behind every event that was selected before
    /// it, and a subscriber is never told of a position before it has been given what the filter selects up to there.
    /// The timer goes on after the log has gone quiet, so the last change that was passed over is reported too.
    /// </remarks>
    private sealed class Observer : IChangeStreamObserver<EventLogDocument<TEvent>>, IDisposable
    {
        private const long Nowhere = -1;

        private readonly Channel<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> _channel;
        private readonly string? _streamId;
        private readonly EventFilter _liveFilter;
        private readonly Timer? _checkpointTimer;
        private readonly CancellationTokenSource _overflowCts = new();
        private bool _hasFailed;
        private long _passedOver = Nowhere;
        private long _reported = Nowhere;
        private int _isReporting;

        public Observer(int queueCapacity, string? streamId, EventFilter liveFilter, TimeSpan checkpointInterval)
        {
            _streamId = streamId;
            _liveFilter = liveFilter;

            // The change stream writes events, and the timer checkpoints. A checkpoint is only queued when the queue is
            // empty, so there is never more than one, and it has a place of its own: the queue always holds as many
            // events as it was asked to.
            var hasCheckpoints = liveFilter is not AllEventsFilter;

            _channel = Channel.CreateBounded<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>>(
                new BoundedChannelOptions(hasCheckpoints ? queueCapacity + 1 : queueCapacity)
                {
                    SingleWriter = false,
                    SingleReader = true
                });

            // A subscription without a filter passes over nothing.
            if (hasCheckpoints)
            {
                _checkpointTimer = new Timer(
                    static x => ((Observer)x!).ReportPassedOver(),
                    this,
                    checkpointInterval,
                    checkpointInterval);
            }
        }

        public ChannelReader<SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>> Reader =>
            _channel.Reader;

        public CancellationToken OverflowToken => _overflowCts.Token;

        public ValueTask OnNextAsync(EventLogDocument<TEvent> change, CancellationToken cancellationToken)
        {
            // One that has failed is waiting to say so, and a change that it could not queue would be taken for
            // falling behind.
            if (_hasFailed)
                return ValueTask.CompletedTask;

            SubscriptionMessage<TEvent, string, StreamPosition, LogPosition> message;

            try
            {
                // A subscription to a stream has nothing to say of the rest of the log.
                if (_streamId is not null && change.StreamId != _streamId)
                    return ValueTask.CompletedTask;

                if (!_liveFilter.Matches(change))
                {
                    // In the position that the subscription resumes by.
                    var position = _streamId is null ? change.Position : change.StreamPosition;
                    Volatile.Write(ref _passedOver, position);
                    return ValueTask.CompletedTask;
                }

                // The document is only good for the length of this call, so the event is taken from it now. It is
                // decoded once, however many observers take it.
                message = SubscriptionMessage.Event(change.DecodedEvent);
            }
            catch (Exception ex)
            {
                // An event that cannot be decoded fails this subscription, as it would a read, and no other.
                _hasFailed = true;
                _channel.Writer.TryComplete(ex);
                return ValueTask.CompletedTask;
            }

            if (_channel.Writer.TryWrite(message) || _overflowCts.IsCancellationRequested)
                return ValueTask.CompletedTask;

            _overflowCts.Cancel();
            _channel.Writer.TryComplete(new OperationCanceledException(_overflowCts.Token));

            return ValueTask.CompletedTask;
        }

        // On the timer. With events in the queue there is nothing to add: each says where it is, and the next tick
        // will do. Nor is a checkpoint worth falling behind for, so one that cannot be queued is left for the next.
        private void ReportPassedOver()
        {
            // One tick at a time, should one ever be slow enough to meet the next.
            if (Interlocked.Exchange(ref _isReporting, 1) != 0)
                return;

            try
            {
                var position = Volatile.Read(ref _passedOver);

                if (position <= _reported || _channel.Reader.Count > 0)
                    return;

                var checkpoint = _streamId is null
                    ? SubscriptionMessage.LogCheckpoint<TEvent, string, StreamPosition, LogPosition>(
                        LogPosition.FromInt64(position))
                    : SubscriptionMessage.StreamCheckpoint<TEvent, string, StreamPosition, LogPosition>(
                        StreamPosition.FromInt64(position));

                if (_channel.Writer.TryWrite(checkpoint))
                    _reported = position;
            }
            finally
            {
                Volatile.Write(ref _isReporting, 0);
            }
        }

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            _channel.Writer.TryComplete(exception);
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            _checkpointTimer?.Dispose();
            _overflowCts.Dispose();
        }
    }
}