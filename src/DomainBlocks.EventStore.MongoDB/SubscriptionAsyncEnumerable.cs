using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

internal class SubscriptionAsyncEnumerable<TEvent, TPos>(
    SubscriptionOrigin<TPos>? origin,
    SubscriptionOptions? options,
    FilterDefinition<BsonDocument> catchUpFilter,
    Func<ReadEventContext<string, StreamPosition, LogPosition>, bool> liveFilter,
    string positionFieldName,
    Func<ReadEventContext<string, StreamPosition, LogPosition>, TPos> positionSelector,
    IMongoCollection<BsonDocument> eventLog,
    RefCountedChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject,
    IEventDecoder<TEvent, BsonValue, BsonValue> eventDecoder) :
    IAsyncEnumerable<SubscriptionMessage>
    where TEvent : notnull
    where TPos : struct, IPosition<TPos>
{
    private readonly SubscriptionOrigin<TPos> _origin = origin ?? SubscriptionOrigin.End<TPos>();
    private readonly SubscriptionOptions _options = options ?? SubscriptionOptions.Default;

    public async IAsyncEnumerator<SubscriptionMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var resumeOrigin = _origin;

        while (true)
        {
            using var observer = new Observer(_options.QueueCapacity);
            await using var _ = changeStreamSubject.Attach(observer).ConfigureAwait(false);

            var enumerator = ReadAllAsync(resumeOrigin, observer, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            await using (enumerator.ConfigureAwait(false))
            {
                while (await MoveNextAsync(enumerator, observer.OverflowToken))
                {
                    var message = enumerator.Current;
                    yield return message;

                    if (message is SubscriptionMessage.Event<ReadEvent<TEvent, string, StreamPosition, LogPosition>> e)
                        resumeOrigin = SubscriptionOrigin.After(positionSelector(e.Value.Context));
                }
            }

            if (observer.OverflowToken.IsCancellationRequested)
                yield return new SubscriptionMessage.FellBehind();
            else
                yield break;
        }

        static async ValueTask<bool> MoveNextAsync(
            IAsyncEnumerator<SubscriptionMessage> enumerator,
            CancellationToken overflowToken)
        {
            try
            {
                return await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (overflowToken.IsCancellationRequested)
            {
                return false;
            }
        }
    }

    private async IAsyncEnumerable<SubscriptionMessage> ReadAllAsync(
        SubscriptionOrigin<TPos> resumeOrigin,
        Observer observer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LogPosition? highWaterMark;

        using (var catchUpCts = CancellationTokenSource.CreateLinkedTokenSource(
                   cancellationToken,
                   observer.OverflowToken))
        {
            highWaterMark = await GetHighWaterMarkAsync(catchUpCts.Token).ConfigureAwait(false);
            if (highWaterMark is not null)
            {
                await foreach (var doc in ReadCatchUpAsync(resumeOrigin, highWaterMark.Value, catchUpCts.Token)
                                   .ConfigureAwait(false))
                {
                    yield return SubscriptionMessage.Event.Create(eventDecoder.Decode(doc));
                }
            }
        }

        yield return new SubscriptionMessage.CaughtUp();

        await foreach (var doc in observer.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var readEvent = eventDecoder.Decode(doc);
            var context = readEvent.Context;

            if (context.LogPosition.Value <= highWaterMark?.Value || !liveFilter(context))
                continue;

            yield return SubscriptionMessage.Event.Create(readEvent);
        }
    }

    private async Task<LogPosition?> GetHighWaterMarkAsync(CancellationToken cancellationToken)
    {
        var projection = Builders<BsonDocument>.Projection.Include(EventLogEntry.FieldNames.Position);

        var result = await eventLog
            .Find(Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending(EventLogEntry.FieldNames.Position))
            .Limit(1)
            .Project(projection)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return result?[EventLogEntry.FieldNames.Position].AsInt64 is { } value ? LogPosition.FromInt64(value) : null;
    }

    private async IAsyncEnumerable<BsonDocument> ReadCatchUpAsync(
        SubscriptionOrigin<TPos> resumeOrigin,
        LogPosition highWaterMark,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (resumeOrigin is SubscriptionOrigin<TPos>.End)
            yield break;

        var filter = catchUpFilter;

        if (resumeOrigin is SubscriptionOrigin<TPos>.After after)
            filter &= Builders<BsonDocument>.Filter.Gt(positionFieldName, after.Position.Value);

        filter &= Builders<BsonDocument>.Filter.Lte(EventLogEntry.FieldNames.Position, highWaterMark.Value);

        using var cursor = await eventLog
            .Find(filter)
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