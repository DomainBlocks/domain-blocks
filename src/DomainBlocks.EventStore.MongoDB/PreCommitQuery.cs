using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

internal sealed class PreCommitQuery(IMongoCollection<BsonDocument> eventLog)
{
    // Sorting on the unique stream index's fields lets $first find each stream's head with one index lookup (a
    // DISTINCT_SCAN), where a $max would read every index entry of each stream.
    private static readonly BsonDocument StreamHeadsSortStage = new("$sort", new BsonDocument
    {
        { EventLogEntry.FieldNames.StreamId, -1 },
        { EventLogEntry.FieldNames.StreamPosition, -1 }
    });

    private static readonly BsonDocument StreamHeadsGroupStage = new("$group", new BsonDocument
    {
        { "_id", $"${EventLogEntry.FieldNames.StreamId}" },
        { "version", new BsonDocument("$first", $"${EventLogEntry.FieldNames.StreamPosition}") }
    });

    private readonly IMongoCollection<BsonDocument> _eventLog = eventLog
        .WithReadConcern(ReadConcern.Majority)
        .WithReadPreference(ReadPreference.Primary);

    private readonly HashSet<BsonValue> _dedup = [];
    private readonly BsonArray _commitIds = [];
    private readonly BsonArray _streamIds = [];

    public void AddCommitId(BsonValue commitId)
    {
        if (_dedup.Add(commitId))
            _commitIds.Add(commitId);
    }

    public void AddStreamId(BsonValue streamId)
    {
        if (_dedup.Add(streamId))
            _streamIds.Add(streamId);
    }

    public void Reset()
    {
        _dedup.Clear();
        _commitIds.Clear();
        _streamIds.Clear();
    }

    public async Task ExecuteIntoAsync(
        HashSet<Guid> existingCommitIds,
        Dictionary<string, long> headStreamVersions,
        CancellationToken cancellationToken = default)
    {
        var commitIdsTask = FindExistingCommitIdsAsync(existingCommitIds, cancellationToken);
        var versionsTask = FindHeadStreamVersionsAsync(headStreamVersions, cancellationToken);

        await Task.WhenAll(commitIdsTask, versionsTask).ConfigureAwait(false);
    }

    private async Task FindExistingCommitIdsAsync(HashSet<Guid> existingCommitIds, CancellationToken cancellationToken)
    {
        if (_commitIds.Count == 0)
            return;

        var filter = Builders<BsonDocument>.Filter.In(EventLogEntry.FieldNames.CommitId, _commitIds);

        var cursor = await _eventLog
            .DistinctAsync<BsonValue>(EventLogEntry.FieldNames.CommitId, filter, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await cursor.ForEachAsync(x => existingCommitIds.Add(x.AsGuid), cancellationToken).ConfigureAwait(false);
    }

    private async Task FindHeadStreamVersionsAsync(
        Dictionary<string, long> headStreamVersions,
        CancellationToken cancellationToken)
    {
        var pipeline = new[]
        {
            new BsonDocument(
                "$match",
                new BsonDocument(EventLogEntry.FieldNames.StreamId, new BsonDocument("$in", _streamIds))),

            StreamHeadsSortStage,
            StreamHeadsGroupStage
        };

        // An aggregate's first batch holds 101 documents unless the command sets a batch size, and the server leaves
        // the cursor open when the results fill the batch exactly. The query returns at most one head per stream, so a
        // batch size one above the number of streams returns every head in the first reply and closes the cursor.
        var options = new AggregateOptions { BatchSize = _streamIds.Count + 1 };

        var cursor = await _eventLog
            .AggregateAsync<BsonDocument>(pipeline, options, cancellationToken)
            .ConfigureAwait(false);

        await cursor
            .ForEachAsync(x => headStreamVersions[x["_id"].AsString] = x["version"].AsInt64, cancellationToken)
            .ConfigureAwait(false);
    }
}