using DomainBlocks.EventStore.MongoDB.Schema;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

internal sealed class PreAppendQuery(IMongoCollection<BsonDocument> eventLog)
{
    private static readonly BsonDocument MaxStreamVersionsGroupStage = new("$group", new BsonDocument
    {
        { "_id", $"${EventLogEntry.FieldNames.StreamId}" },
        { "version", new BsonDocument("$max", $"${EventLogEntry.FieldNames.StreamVersion}") }
    });

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

    public async Task ExecuteAsync(
        HashSet<Guid> existingCommitIds,
        Dictionary<string, long> headStreamVersions,
        CancellationToken cancellationToken)
    {
        var commitIdFilter = new BsonDocument(
            EventLogEntry.FieldNames.CommitId,
            new BsonDocument("$in", _commitIds));

        var maxStreamVersionsPipeline = new[]
        {
            new BsonDocument(
                "$match",
                new BsonDocument(EventLogEntry.FieldNames.StreamId, new BsonDocument("$in", _streamIds))),

            MaxStreamVersionsGroupStage
        };

        var commitIdsTask = eventLog
            .Distinct<BsonValue>(
                EventLogEntry.FieldNames.CommitId,
                commitIdFilter,
                cancellationToken: cancellationToken)
            .ForEachAsync(x => existingCommitIds.Add(x.AsGuid), cancellationToken);

        var versionsTask = eventLog
            .Aggregate<BsonDocument>(maxStreamVersionsPipeline, cancellationToken: cancellationToken)
            .ForEachAsync(x => headStreamVersions[x["_id"].AsString] = x["version"].AsInt64, cancellationToken);

        await Task.WhenAll(commitIdsTask, versionsTask).ConfigureAwait(false);

        // _logger.LogDebug(
        //     "Prepare append complete: Found {DuplicateCount} duplicate(s), loaded {StreamCount} stream version(s)",
        //     _duplicateCommitIds.Count,
        //     _headStreamVersions.Count);
    }
}