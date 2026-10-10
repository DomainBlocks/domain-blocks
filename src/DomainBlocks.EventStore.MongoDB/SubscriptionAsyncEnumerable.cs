using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
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
    private readonly FilterDefinition<BsonDocument> _catchUpFilter;
    private readonly EventFilter _liveFilter;
    private readonly string _positionFieldName;
    private readonly Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> _positionSelector;
    private readonly Func<long, TPos> _positionFactory;
    private readonly SubscriptionOrigin<TPos> _origin;
    private readonly SubscriptionOptions _options;
    private readonly ILogger? _logger;
    private readonly string _correlationId;

    public SubscriptionAsyncEnumerable(
        IMongoCollection<BsonDocument> eventLog,
        RefCountedChangeStreamSubject<EventLogDocument<TEvent>> changeStreamSubject,
        IEventDecoder<TEvent, BsonValue, BsonValue> eventDecoder,
        FilterDefinition<BsonDocument> catchUpFilter,
        EventFilter liveFilter,
        string positionFieldName,
        Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> positionSelector,
        Func<long, TPos> positionFactory,
        SubscriptionOrigin<TPos>? origin,
        SubscriptionOptions? options,
        ILogger? logger)
    {
        _origin = origin ?? SubscriptionOrigin.End;
        _options = options ?? SubscriptionOptions.Default;
        _catchUpFilter = catchUpFilter;
        _liveFilter = liveFilter;
        _positionFieldName = positionFieldName;
        _positionSelector = positionSelector;
        _positionFactory = positionFactory;
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

            // A subscription from the end is pinned to the last position at the time it starts. A cycle that restarts
            // before delivering an event then resumes from that position, rather than from a later end that would skip
            // the events appended in between.
            var resumeOrigin = _origin is SubscriptionOrigin<TPos>.End
                ? await PinEndAsync(cancellationToken).ConfigureAwait(false)
                : _origin;

            while (true)
            {
                using var observer = new Observer(_options.QueueCapacity, _liveFilter);
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

    private async Task<SubscriptionOrigin<TPos>> PinEndAsync(CancellationToken cancellationToken)
    {
        try
        {
            var projection = Builders<BsonDocument>.Projection.Include(_positionFieldName);

            var last = await _eventLog
                .Find(_catchUpFilter)
                .Sort(Builders<BsonDocument>.Sort.Descending(_positionFieldName))
                .Limit(1)
                .Project(projection)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return last?[_positionFieldName].AsInt64 is { } position
                ? SubscriptionOrigin.After(_positionFactory(position))
                : SubscriptionOrigin.Start;
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
                await foreach (var doc in ReadCatchUpAsync(session, resumeOrigin, highWaterMark.Value, catchUpCts.Token)
                                   .ConfigureAwait(false))
                {
                    yield return SubscriptionMessage.Event(_eventDecoder.Decode(doc));
                }
            }
        }

        _logger?.SubscriptionCaughtUp(_correlationId);

        yield return SubscriptionMessage<TEvent, string, StreamPosition, LogPosition>.CaughtUp;

        await foreach (var e in observer.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            // Catching up has delivered it.
            if (e.Context.LogPosition.Value <= highWaterMark?.Value)
                continue;

            yield return SubscriptionMessage.Event(e);
        }
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
        var filter = _catchUpFilter;

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
    /// Buffers the live events that a subscription selects, for one subscription cycle. Never blocks the change
    /// stream: an overflow cancels the overflow token and completes the channel so that the cycle ends and a new one
    /// starts from the last position.
    /// </summary>
    internal sealed class Observer(int queueCapacity, EventFilter liveFilter) :
        IChangeStreamObserver<EventLogDocument<TEvent>>,
        IDisposable
    {
        private readonly Channel<ReadEvent<TEvent, string, StreamPosition, LogPosition>> _channel =
            Channel.CreateBounded<ReadEvent<TEvent, string, StreamPosition, LogPosition>>(
                new BoundedChannelOptions(queueCapacity)
                {
                    SingleWriter = true,
                    SingleReader = true
                });

        private readonly CancellationTokenSource _overflowCts = new();
        private bool _isStopped;

        public ChannelReader<ReadEvent<TEvent, string, StreamPosition, LogPosition>> Reader => _channel.Reader;

        public CancellationToken OverflowToken => _overflowCts.Token;

        public ValueTask OnNextAsync(EventLogDocument<TEvent> document, CancellationToken cancellationToken)
        {
            // An observer that has failed or signaled an overflow takes nothing more. It stays attached until the
            // subscription replaces it, and a document that it selected would be decoded only to be dropped.
            if (_isStopped || !liveFilter.Matches(document))
                return ValueTask.CompletedTask;

            ReadEvent<TEvent, string, StreamPosition, LogPosition> e;

            try
            {
                // The document is only valid during this call, so the event is taken from it now.
                e = document.DecodedEvent;
            }
            catch (Exception ex)
            {
                // An event that cannot be decoded fails this subscription, as it would a read. Thrown to the change
                // stream, it would detach the observer without telling the subscriber.
                _isStopped = true;
                _channel.Writer.TryComplete(ex);

                return ValueTask.CompletedTask;
            }

            if (_channel.Writer.TryWrite(e))
                return ValueTask.CompletedTask;

            _isStopped = true;
            _overflowCts.Cancel();
            _channel.Writer.TryComplete(new OperationCanceledException(_overflowCts.Token));

            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken)
        {
            _channel.Writer.TryComplete(exception);
            return ValueTask.CompletedTask;
        }

        public void Dispose() => _overflowCts.Dispose();
    }
}