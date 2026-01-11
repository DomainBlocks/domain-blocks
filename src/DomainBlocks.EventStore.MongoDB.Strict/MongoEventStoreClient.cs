using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public class MongoEventStoreClient<TEvent> : IEventStoreClient<TEvent> where TEvent : notnull
{
    private const string GlobalPositionSequenceId = "global_position";

    private readonly IMongoCollection<StreamCommit> _streamCommitsCollection;
    private readonly SequenceStore _sequenceStore;
    private readonly IEventCodec<TEvent, BsonValue, BsonValue> _eventCodec;

    public MongoEventStoreClient(IMongoClient mongoClient, MongoEventStoreClientOptions<TEvent> options)
    {
        var collectionOptions = options.CollectionOptions;
        var db = mongoClient.GetDatabase(collectionOptions.DatabaseName);
        var countersCollection = db.GetCollection<BsonDocument>(collectionOptions.SequencesCollectionName);

        _streamCommitsCollection = db.GetCollection<StreamCommit>(collectionOptions.StreamCommitsCollectionName);
        _sequenceStore = new SequenceStore(countersCollection);
        _eventCodec = options.EventCodec;
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? appendOptions = null,
        CancellationToken cancellationToken = default)
    {
        appendOptions ??= AppendToStreamOptions.Default;
        var expectedState = appendOptions.ExpectedState;
        var currentState = await GetStreamStateAsync(streamId, cancellationToken).ConfigureAwait(false);

        if (!expectedState.Matches(currentState))
            throw new StreamAppendConflictException(streamId, expectedState, currentState);

        var startVersion = currentState.IsStreamExists
            ? new StreamVersion(currentState.Version.Value.Value + 1)
            : new StreamVersion(0);

        var eventDocs = ToEventDocuments(events).ToArray();

        var globalPositionRange = await _sequenceStore
            .NextRangeAsync(GlobalPositionSequenceId, eventDocs.Length, cancellationToken)
            .ConfigureAwait(false);

        var streamCommit = new StreamCommit
        {
            StreamId = streamId,
            StartStreamVersion = Convert.ToInt64(startVersion.Value),
            StartGlobalPosition = globalPositionRange.Start,
            CommittedAtUtc = DateTime.UtcNow,
            Events = eventDocs
        };

        try
        {
            await _streamCommitsCollection
                .InsertOneAsync(streamCommit, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Consider automatically retrying if the original expectation was Any/StreamExists.
            throw new StreamAppendConflictException(streamId, expectedState, innerException: ex);
        }
    }

    public async IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? readOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        readOptions ??= ReadStreamOptions.Default;
        var position = readOptions.Position;
        var direction = readOptions.Direction;

        // Edge cases that represent an empty sequence of events.
        if (position.IsStart && direction == StreamReadDirection.Backward ||
            position.IsEnd && direction == StreamReadDirection.Forward)
        {
            if (readOptions.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
                !await StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
            {
                throw new StreamNotFoundException(streamId);
            }

            yield break;
        }

        var filter = Builders<StreamCommit>.Filter.Eq(x => x.StreamId, streamId);

        if (position.IsSpecificVersion)
        {
            var versionValue = checked((long)position.Version.Value.Value);

            var versionFilter = direction == StreamReadDirection.Forward
                ? Builders<StreamCommit>.Filter.Gte(x => x.StartStreamVersion, versionValue)
                : Builders<StreamCommit>.Filter.Lte(x => x.StartStreamVersion, versionValue);

            filter = Builders<StreamCommit>.Filter.And(filter, versionFilter);
        }

        var sort = direction == StreamReadDirection.Forward
            ? Builders<StreamCommit>.Sort.Ascending(x => x.StartStreamVersion)
            : Builders<StreamCommit>.Sort.Descending(x => x.StartStreamVersion);

        using var cursor = await _streamCommitsCollection
            .Find(filter)
            .Sort(sort)
            .Limit(readOptions.MaxCount)
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        var isEmpty = true;

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var doc in cursor.Current)
            {
                if (direction == StreamReadDirection.Forward)
                {
                    var startVersionValue = Convert.ToUInt64(doc.StartStreamVersion);

                    for (uint i = 0; i < doc.Events.Length; i++)
                    {
                        isEmpty = false;
                        var eventDoc = doc.Events[i];

                        var (@event, metadata) = _eventCodec.Decode(
                            eventDoc.EventName,
                            eventDoc.EventData,
                            eventDoc.Metadata);

                        var version = new StreamVersion(startVersionValue + i);
                        var context = new ReadEventContext(streamId, version, doc.CommittedAtUtc);

                        yield return ReadEvent.Create(@event, metadata, context);
                    }
                }
                else
                {
                    var endVersionValue = Convert.ToUInt64(doc.EndStreamVersion);
                }
            }
        }

        if (isEmpty && readOptions.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw)
            throw new StreamNotFoundException(streamId);
    }

    private async Task<StreamState> GetStreamStateAsync(string streamId, CancellationToken cancellationToken)
    {
        var latestVersion = await _streamCommitsCollection
            .Find(Builders<StreamCommit>.Filter.Eq(x => x.StreamId, streamId))
            .Sort(Builders<StreamCommit>.Sort.Descending(x => x.StartStreamVersion))
            .Project(x => (long?)x.StartStreamVersion + x.Events.Length - 1)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return latestVersion.HasValue
            ? StreamState.StreamExists(new StreamVersion(Convert.ToUInt64(latestVersion.Value)))
            : StreamState.StreamDoesNotExist;
    }

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        var streamState = await GetStreamStateAsync(streamId, cancellationToken).ConfigureAwait(false);
        return streamState.IsStreamExists;
    }

    private IEnumerable<EventDocument> ToEventDocuments(IEnumerable<AppendEvent<TEvent>> events)
    {
        var encoder = _eventCodec.CreateEncoder();

        foreach (var @event in events)
        {
            var (eventName, eventData, metadata) = encoder.Encode(@event);

            yield return new EventDocument
            {
                EventName = eventName,
                EventData = eventData,
                Metadata = metadata ?? BsonNull.Value
            };
        }
    }
}