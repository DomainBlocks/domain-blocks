using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public class MongoEventStoreClient<TEvent> : IEventStoreClient<TEvent> where TEvent : notnull
{
    private const string GlobalPositionSequenceId = "global_position";

    private readonly IMongoCollection<StreamCommit> _commitsCollection;
    private readonly SequenceAllocator _sequenceAllocator;
    private readonly IEventCodec<TEvent, BsonValue, BsonValue> _eventCodec;

    public MongoEventStoreClient(IMongoClient mongoClient, MongoEventStoreClientOptions<TEvent> options)
    {
        var collectionOptions = options.CollectionOptions;
        var db = mongoClient.GetDatabase(collectionOptions.DatabaseName);
        var countersCollection = db.GetCollection<BsonDocument>(collectionOptions.SequencesCollectionName);

        _commitsCollection = db.GetCollection<StreamCommit>(collectionOptions.StreamCommitsCollectionName);
        _sequenceAllocator = new SequenceAllocator(countersCollection);
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

        var eventDocuments = ToEventDocuments(events).ToArray();
        if (eventDocuments.Length == 0)
            return;

        var globalPositionAllocation = await _sequenceAllocator
            .AllocateNextAsync(GlobalPositionSequenceId, eventDocuments.Length, cancellationToken)
            .ConfigureAwait(false);

        // We may eventually enforce limits on:
        //  - max events per commit
        //  - max event size (for any single event)
        //  - max commit BSON size (Mongo doc limit is 16MB; use a safety margin)
        var commit = new StreamCommit
        {
            Id = ObjectId.GenerateNewId(),
            StreamId = streamId,
            StartStreamVersion = Convert.ToInt64(startVersion.Value),
            StartGlobalPosition = globalPositionAllocation.Start,
            CommittedAtUtc = DateTime.UtcNow,
            Events = eventDocuments
        };

        try
        {
            await _commitsCollection
                .InsertOneAsync(commit, cancellationToken: cancellationToken)
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

        using var cursor = await _commitsCollection
            .Find(filter)
            .Sort(sort)
            .Limit(readOptions.MaxCount)
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        var isEmpty = true;

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var commit in cursor.Current)
            {
                if (commit.Events.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Commit '{streamId}@v{commit.StartStreamVersion}' contains no events.");
                }

                isEmpty = false;

                foreach (var (eventDoc, index) in EnumerateEvents(commit.Events, direction))
                {
                    var (@event, metadata) = _eventCodec.Decode(
                        eventDoc.EventName,
                        eventDoc.EventData,
                        eventDoc.Metadata);

                    var version = new StreamVersion(checked((ulong)commit.StartStreamVersion + (ulong)index));
                    var context = new ReadEventContext(streamId, version, commit.CommittedAtUtc);

                    yield return ReadEvent.Create(@event, metadata, context);
                }
            }
        }

        if (isEmpty && readOptions.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw)
            throw new StreamNotFoundException(streamId);

        yield break;

        static IEnumerable<(EventDocument, int)> EnumerateEvents(EventDocument[] events, StreamReadDirection direction)
        {
            if (direction == StreamReadDirection.Forward)
            {
                for (var i = 0; i < events.Length; i++)
                    yield return (events[i], i);
            }
            else
            {
                for (var i = events.Length - 1; i >= 0; i--)
                    yield return (events[i], i);
            }
        }
    }

    private async Task<StreamState> GetStreamStateAsync(string streamId, CancellationToken cancellationToken)
    {
        var latestVersion = await _commitsCollection
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