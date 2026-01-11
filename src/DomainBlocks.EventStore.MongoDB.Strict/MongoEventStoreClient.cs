using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public class MongoEventStoreClient<TEvent> : IEventStoreClient<TEvent> where TEvent : notnull
{
    private const string GlobalPositionSequenceId = "global_position";

    private readonly IMongoCollection<EventDocument> _eventsCollection;
    private readonly IMongoCollection<StreamCommit> _streamCommitsCollection;
    private readonly SequenceStore _sequenceStore;
    private readonly IEventDocumentCodec<TEvent> _eventDocumentCodec;

    public MongoEventStoreClient(IMongoClient mongoClient, MongoEventStoreClientOptions<TEvent> options)
    {
        var collectionOptions = options.CollectionOptions;
        var db = mongoClient.GetDatabase(collectionOptions.DatabaseName);
        var countersCollection = db.GetCollection<BsonDocument>(collectionOptions.SequencesCollectionName);

        _eventsCollection = db.GetCollection<EventDocument>(collectionOptions.EventsCollectionName);
        _streamCommitsCollection = db.GetCollection<StreamCommit>(collectionOptions.StreamCommitsCollectionName);
        _sequenceStore = new SequenceStore(countersCollection);
        _eventDocumentCodec = options.EventDocumentCodec;
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

        var commitId = Guid.NewGuid();
        var committedAtUtc = DateTime.UtcNow;
        var documents = ToEventDocuments(events, streamId, startVersion, commitId, committedAtUtc).ToArray();

        var globalPositionRange = await _sequenceStore
            .NextRangeAsync(GlobalPositionSequenceId, documents.Length, cancellationToken)
            .ConfigureAwait(false);

        var streamCommit = new StreamCommit
        {
            StreamId = streamId,
            StartStreamVersion = Convert.ToInt64(startVersion.Value),
            StartGlobalPosition = globalPositionRange.Start,
            EventCount = documents.Length,
            CommitId = commitId,
            CommittedAtUtc = committedAtUtc
        };

        try
        {
            await _eventsCollection
                .InsertManyAsync(documents, new InsertManyOptions { IsOrdered = true }, cancellationToken)
                .ConfigureAwait(false);

            await _streamCommitsCollection
                .InsertOneAsync(streamCommit, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MongoBulkWriteException ex) when
            (ex.WriteErrors?.Any(e => e.Category == ServerErrorCategory.DuplicateKey) is true)
        {
            // Consider automatically retrying if the original expectation was Any/StreamExists.
            throw new StreamAppendConflictException(streamId, expectedState, innerException: ex);
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

        var commitsFilter = Builders<StreamCommit>.Filter.Eq(x => x.StreamId, streamId);

        if (position.IsSpecificVersion)
        {
            var versionValue = checked((long)position.Version.Value.Value);

            var versionFilter = direction == StreamReadDirection.Forward
                ? Builders<StreamCommit>.Filter.Gte(x => x.StartStreamVersion, versionValue)
                : Builders<StreamCommit>.Filter.Lte(x => x.StartStreamVersion, versionValue);

            commitsFilter = Builders<StreamCommit>.Filter.And(commitsFilter, versionFilter);
        }

        var eventsSort = direction == StreamReadDirection.Forward
            ? Builders<EventDocument>.Sort.Ascending(x => x.StreamVersion)
            : Builders<EventDocument>.Sort.Descending(x => x.StreamVersion);

        var query = _streamCommitsCollection
            .Aggregate()
            .Match(commitsFilter)
            .Lookup<EventDocument, BsonDocument>(
                foreignCollectionName: _eventsCollection.CollectionNamespace.CollectionName,
                localField: nameof(StreamCommit.CommitId),
                foreignField: nameof(EventDocument.CommitId),
                @as: "events")
            .Unwind("events")
            .ReplaceRoot<EventDocument>("$events")
            .Sort(eventsSort);

        if (readOptions.MaxCount.HasValue)
            query = query.Limit(readOptions.MaxCount.Value);

        using var cursor = await query.ToCursorAsync(cancellationToken).ConfigureAwait(false);
        var isEmpty = true;

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var doc in cursor.Current)
            {
                isEmpty = false;
                yield return _eventDocumentCodec.Decode(doc);
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
            .Project(x => (long?)x.StartStreamVersion + x.EventCount - 1)
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

    private IEnumerable<EventDocument> ToEventDocuments(
        IEnumerable<AppendEvent<TEvent>> events,
        string streamId,
        StreamVersion startVersion,
        Guid commitId,
        DateTime createdAtUtc)
    {
        var nextVersionValue = startVersion.Value;
        var encoder = _eventDocumentCodec.CreateEncoder();

        foreach (var @event in events)
        {
            var streamVersion = new StreamVersion(nextVersionValue++);
            yield return encoder.Encode(@event, streamId, streamVersion, commitId, createdAtUtc);
        }
    }
}