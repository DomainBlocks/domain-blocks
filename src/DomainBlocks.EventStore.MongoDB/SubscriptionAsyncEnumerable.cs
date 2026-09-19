using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Codecs;
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
    private readonly RefCountedChangeStreamSubject<ChangeStreamDocument<BsonDocument>> _changeStreamSubject;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _eventDecoder;
    private readonly FilterDefinition<BsonDocument> _catchUpFilter;
    private readonly Func<ReadEventContext<string, StreamPosition, LogPosition>, bool> _livePredicate;
    private readonly string _positionFieldName;
    private readonly Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> _positionSelector;
    private readonly SubscriptionOrigin<TPos> _origin;
    private readonly SubscriptionOptions _options;
    private readonly ILogger? _logger;
    private readonly string _correlationId;

    public SubscriptionAsyncEnumerable(
        IMongoCollection<BsonDocument> eventLog,
        RefCountedChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject,
        IEventDecoder<TEvent, BsonValue, BsonValue> eventDecoder,
        FilterDefinition<BsonDocument> catchUpFilter,
        Func<ReadEventContext<string, StreamPosition, LogPosition>, bool> livePredicate,
        string positionFieldName,
        Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> positionSelector,
        SubscriptionOrigin<TPos> origin,
        SubscriptionOptions? options,
        ILogger? logger)
    {
        _origin = origin.Resolve();
        _options = options ?? SubscriptionOptions.Default;
        _catchUpFilter = catchUpFilter;
        _livePredicate = livePredicate;
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
                using var observer = new Observer(_options.QueueCapacity);
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
                    yield return SubscriptionMessage.FellBehind;
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
                    yield return _eventDecoder.Decode(doc);
                }
            }
        }

        _logger?.SubscriptionCaughtUp(_correlationId);

        yield return SubscriptionMessage.CaughtUp;

        await foreach (var doc in observer.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var readEvent = _eventDecoder.Decode(doc);
            var context = readEvent.Context;

            if (context.LogPosition.Value <= highWaterMark?.Value || !_livePredicate(context))
                continue;

            yield return readEvent;
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
        if (resumeOrigin is SequenceEnd)
            yield break;

        var filter = _catchUpFilter;

        if (resumeOrigin.TryGetValue(out SubscriptionOrigin<TPos>.After after))
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

    private sealed class Observer(int queueCapacity) :
        IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>,
        IDisposable
    {
        private readonly Channel<BsonDocument> _channel = Channel.CreateBounded<BsonDocument>(
            new BoundedChannelOptions(queueCapacity)
            {
                SingleWriter = true,
                SingleReader = true
            });

        private readonly CancellationTokenSource _overflowCts = new();

        public ChannelReader<BsonDocument> Reader => _channel.Reader;

        public CancellationToken OverflowToken => _overflowCts.Token;

        public ValueTask OnNextAsync(ChangeStreamDocument<BsonDocument> change, CancellationToken cancellationToken)
        {
            if (_channel.Writer.TryWrite(change.FullDocument) || _overflowCts.IsCancellationRequested)
                return ValueTask.CompletedTask;

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