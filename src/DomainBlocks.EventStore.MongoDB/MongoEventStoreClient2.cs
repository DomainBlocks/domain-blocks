using System.Diagnostics;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.Abstractions.New;
using DomainBlocks.MongoDB.Sequencing;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public class MongoEventStoreClient2<TEvent>(
    IMongoSequencedAppender<BsonDocument, AppendToStreamContext> sequencedAppender,
    IMongoCollection<BsonDocument> eventLog,
    EventCodec<TEvent, BsonValue, BsonValue> eventCodec) :
    IMongoEventStoreClient2<TEvent>
    where TEvent : notnull
{
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _encoder = eventCodec.Encoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _decoder = eventCodec.Decoder;

    public async Task AppendToStreamAsync(
        string streamId,
        ExpectedStreamState2<StreamPosition> expectedState,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AppendToStreamOptions();

        var bsonStreamId = new BsonString(streamId);
        var bsonCommitId = new BsonBinaryData(options.CommitId, GuidRepresentation.Standard);

        var eventDocuments = _encoder
            .Encode(events)
            .Select((x, i) => new BsonDocument
            {
                { EventLogEntry.FieldNames.StreamId, bsonStreamId },
                { EventLogEntry.FieldNames.CommitId, bsonCommitId },
                { EventLogEntry.FieldNames.CommitIndex, i },
                { EventLogEntry.FieldNames.EventName, x.EventName },
                { EventLogEntry.FieldNames.EventData, x.EventData },
                { EventLogEntry.FieldNames.Metadata, x.Metadata ?? BsonNull.Value }
            });

        var context = new AppendToStreamContext(options.CommitId, streamId, options.ExpectedStreamState);
        var appendOptions = new AppendOptions { Timeout = options.Timeout };

        await sequencedAppender
            .AppendAsync(eventDocuments, context, appendOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public IAsyncEnumerable<ReadEvent2<TEvent, string, StreamPosition, LogPosition>> ReadAll(
        ReadDefinition<LogPosition> definition,
        ReadAllOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<ReadEvent2<TEvent, string, StreamPosition, LogPosition>> ReadStream(
        string streamId,
        ReadDefinition<StreamPosition> definition,
        ReadStreamOptions2? options = null)
    {
        return ReadStreamCoreAsync(streamId, definition, options ?? ReadStreamOptions2.Default);
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
        SubscriptionDefinition<LogPosition> definition,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
        string streamId,
        SubscriptionDefinition<StreamPosition> definition,
        SubscriptionOptions? options = null)
    {
        throw new NotImplementedException();
    }

    private async IAsyncEnumerable<ReadEvent2<TEvent, string, StreamPosition, LogPosition>> ReadStreamCoreAsync(
        string streamId,
        ReadDefinition<StreamPosition> definition,
        ReadStreamOptions2 options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);

        var (position, isForward) = definition switch
        {
            ReadDefinition<StreamPosition>.ForwardFromStart => ((ulong?)null, true),
            ReadDefinition<StreamPosition>.ForwardFrom x => (x.Position.Value, true),
            ReadDefinition<StreamPosition>.BackwardFromEnd => (null, false),
            ReadDefinition<StreamPosition>.BackwardFrom x => (x.Position.Value, false),
            _ => throw new UnreachableException($"Unknown ReadDefinition '{definition.GetType().Name}'.")
        };

        if (position.HasValue)
        {
            var versionFilter = isForward
                ? Builders<BsonDocument>.Filter.Gte(EventLogEntry.FieldNames.StreamVersion, position.Value)
                : Builders<BsonDocument>.Filter.Lte(EventLogEntry.FieldNames.StreamVersion, position.Value);

            filter &= versionFilter;
        }

        var sort = isForward
            ? Builders<BsonDocument>.Sort.Ascending(EventLogEntry.FieldNames.StreamVersion)
            : Builders<BsonDocument>.Sort.Descending(EventLogEntry.FieldNames.StreamVersion);

        using var cursor = await eventLog
            .Find(filter)
            .Sort(sort)
            .Limit(options.MaxCount)
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        var isEmpty = true;

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var doc in cursor.Current)
            {
                isEmpty = false;

                var logSequenceNumber = LogPosition.FromInt64(doc["_id"].AsInt64);
                var streamVersion = StreamPosition.FromInt64(doc[EventLogEntry.FieldNames.StreamVersion].AsInt64);
                var eventName = doc[EventLogEntry.FieldNames.EventName].AsString;
                var eventData = doc[EventLogEntry.FieldNames.EventData];
                var metadata = doc[EventLogEntry.FieldNames.Metadata];
                var writtenAtUtc = doc[EventLogEntry.FieldNames.WrittenAtUtc].AsBsonDateTime.ToUniversalTime();

                var (@event, decodedMetadata) = _decoder.Decode(eventName, eventData, metadata);

                yield return ReadEvent2.Create(
                    @event,
                    streamId,
                    decodedMetadata,
                    writtenAtUtc,
                    streamVersion,
                    logSequenceNumber);
            }
        }

        if (isEmpty && options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw)
            throw new StreamNotFoundException(streamId);
    }

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);
        return await eventLog.Find(filter).AnyAsync(cancellationToken).ConfigureAwait(false);
    }
}