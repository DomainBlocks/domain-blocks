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

    public void AddInput(BsonValue commitId, BsonValue streamId)
    {
        if (_dedup.Add(commitId))
            _commitIds.Add(commitId);

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
        var commitIdFilter = Builders<BsonDocument>.Filter.In(EventLogEntry.FieldNames.CommitId, _commitIds);

        var streamHeadsPipeline = new[]
        {
            new BsonDocument(
                "$match",
                new BsonDocument(EventLogEntry.FieldNames.StreamId, new BsonDocument("$in", _streamIds))),

            StreamHeadsSortStage,
            StreamHeadsGroupStage
        };

        var commitIdsTask = _eventLog
            .Distinct<BsonValue>(
                EventLogEntry.FieldNames.CommitId,
                commitIdFilter,
                cancellationToken: cancellationToken)
            .ForEachAsync(x => existingCommitIds.Add(x.AsGuid), cancellationToken);

        var versionsTask = _eventLog
            .Aggregate<BsonDocument>(streamHeadsPipeline, cancellationToken: cancellationToken)
            .ForEachAsync(x => headStreamVersions[x["_id"].AsString] = x["version"].AsInt64, cancellationToken);

        await Task.WhenAll(commitIdsTask, versionsTask).ConfigureAwait(false);
    }
}