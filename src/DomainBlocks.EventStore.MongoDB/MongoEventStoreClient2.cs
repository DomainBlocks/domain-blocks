using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Schema;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class MongoEventStoreClient2<TEvent> :
    IEventStoreClient<TEvent>,
    IAsyncDisposable
    where TEvent : notnull
{
    private readonly IMongoCollection<BsonDocument> _eventLog;
    private readonly SequencedAppender<BsonDocument, AppendToStreamContext> _sequencedAppender;
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _encoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _decoder;
    private readonly ILogger _logger;

    public MongoEventStoreClient2(
        IMongoClient mongoClient,
        EventCodec<TEvent, BsonValue, BsonValue> eventCodec,
        ILogger logger,
        MongoEventStoreClientOptions2? options = null)
    {
        options ??= new MongoEventStoreClientOptions2();

        var db = mongoClient
            .GetDatabase(options.DatabaseName)
            .WithReadConcern(ReadConcern.Majority)
            .WithReadPreference(ReadPreference.Primary)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        var sequenceBinding = new SequenceBinding<BsonDocument>(
            new CollectionNamespace(db.DatabaseNamespace, options.SequencesCollectionName),
            new CollectionNamespace(db.DatabaseNamespace, options.EventLogCollectionName),
            sequenceId: "event_log_seq",
            targetField: "_id");

        _eventLog = db.GetCollection<BsonDocument>(options.EventLogCollectionName);

        _sequencedAppender = new SequencedAppender<BsonDocument, AppendToStreamContext>(
            mongoClient,
            sequenceBinding,
            new AppendToStreamPolicy(_eventLog),
            logger: logger);

        _encoder = eventCodec.Encoder;
        _decoder = eventCodec.Decoder;
        _logger = logger;
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AppendToStreamOptions();

        var bsonCommitId = new BsonBinaryData(options.CommitId, GuidRepresentation.Standard);

        var eventDocuments = _encoder
            .Encode(events)
            .Select((x, i) => new BsonDocument
            {
                { EventLogEntry.FieldNames.StreamId, streamId },
                { EventLogEntry.FieldNames.CommitId, bsonCommitId },
                { EventLogEntry.FieldNames.CommitIndex, i },
                { EventLogEntry.FieldNames.EventName, x.EventName },
                { EventLogEntry.FieldNames.EventData, x.EventData },
                { EventLogEntry.FieldNames.Metadata, x.Metadata ?? BsonNull.Value }
            });

        var context = new AppendToStreamContext(options.CommitId, streamId, options.ExpectedState);

        await _sequencedAppender
            .AppendAsync(eventDocuments, context, new AppendOptions { Timeout = options.Timeout }, cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= ReadStreamOptions.Default;
        var position = options.Position;
        var direction = options.Direction;

        // Edge cases that represent an empty sequence of events.
        if (position.IsStart && direction == StreamReadDirection.Backward ||
            position.IsEnd && direction == StreamReadDirection.Forward)
        {
            if (options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
                !await StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
            {
                throw new StreamNotFoundException(streamId);
            }

            yield break;
        }

        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);

        if (position.IsSpecificVersion)
        {
            var versionValue = position.Version.Value.ToInt64();

            var versionFilter = direction == StreamReadDirection.Forward
                ? Builders<BsonDocument>.Filter.Gte(EventLogEntry.FieldNames.StreamVersion, versionValue)
                : Builders<BsonDocument>.Filter.Lte(EventLogEntry.FieldNames.StreamVersion, versionValue);

            filter &= versionFilter;
        }

        var sort = direction == StreamReadDirection.Forward
            ? Builders<BsonDocument>.Sort.Ascending(EventLogEntry.FieldNames.StreamVersion)
            : Builders<BsonDocument>.Sort.Descending(EventLogEntry.FieldNames.StreamVersion);

        using var cursor = await _eventLog
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

                var globalPosition = LogPosition.FromInt64(doc["_id"].AsInt64);
                var streamVersion = StreamVersion.FromInt64(doc[EventLogEntry.FieldNames.StreamVersion].AsInt64);
                var eventName = doc[EventLogEntry.FieldNames.EventName].AsString;
                var eventData = doc[EventLogEntry.FieldNames.EventData];
                var metadata = doc[EventLogEntry.FieldNames.Metadata];
                var writtenAtUtc = doc[EventLogEntry.FieldNames.WrittenAtUtc].AsBsonDateTime.ToUniversalTime();

                var (@event, decodedMetadata) = _decoder.Decode(eventName, eventData, metadata);
                var context = new ReadEventContext(streamId, streamVersion, writtenAtUtc, globalPosition);

                yield return ReadEvent.Create(@event, decodedMetadata, context);
            }
        }

        if (isEmpty && options.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw)
            throw new StreamNotFoundException(streamId);
    }

    public ValueTask DisposeAsync()
    {
        return _sequencedAppender.DisposeAsync();
    }

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Eq(EventLogEntry.FieldNames.StreamId, streamId);
        return await _eventLog.Find(filter).AnyAsync(cancellationToken).ConfigureAwait(false);
    }
}