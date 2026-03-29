using DomainBlocks.EventStore.MongoDB.Client.Schema;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public partial class EventLogAppender
{
    private static readonly BsonDocument GroupByStreamStage = new("$group", new BsonDocument
    {
        { "_id", $"${FieldNames.StreamId}" },
        { "version", new BsonDocument("$max", $"${FieldNames.StreamVersion}") }
    });

    private readonly BsonDocument _visibilityFilter = initialCommitPosition.HasValue
        ? new BsonDocument("$or", new BsonArray
        {
            new BsonDocument(FieldNames.Epoch, epoch),
            new BsonDocument("_id", new BsonDocument("$lte", initialCommitPosition.Value))
        })
        : new BsonDocument(FieldNames.Epoch, epoch);

    private readonly HashSet<BsonValue> _prefetchDedup = [];
    private readonly BsonArray _prefetchCommitIds = [];
    private readonly BsonArray _prefetchStreamIds = [];

    private async Task PrefetchAsync(CancellationToken cancellationToken)
    {
        _prefetchDedup.Clear();
        _prefetchCommitIds.Clear();
        _prefetchStreamIds.Clear();

        foreach (var request in _requests)
        {
            var commitId = request[FieldNames.CommitId];
            var streamId = request[FieldNames.StreamId];

            if (_prefetchDedup.Add(commitId))
                _prefetchCommitIds.Add(commitId);

            if (_prefetchDedup.Add(streamId))
                _prefetchStreamIds.Add(streamId);
        }

        var commitIdFilter = new BsonDocument("$and", new BsonArray
        {
            new BsonDocument(FieldNames.CommitId, new BsonDocument("$in", _prefetchCommitIds)),
            _visibilityFilter
        });

        var maxStreamVersionsPipeline = new[]
        {
            new BsonDocument("$match",
                new BsonDocument("$and", new BsonArray
                {
                    new BsonDocument(FieldNames.StreamId, new BsonDocument("$in", _prefetchStreamIds)),
                    _visibilityFilter
                })),

            GroupByStreamStage
        };

        var duplicatesTask = _eventLog
            .Distinct<BsonValue>(
                FieldNames.CommitId,
                commitIdFilter,
                cancellationToken: cancellationToken)
            .ForEachAsync(x => _duplicateCommitIds.Add(x.AsGuid), cancellationToken);

        var versionsTask = _eventLog
            .Aggregate<BsonDocument>(maxStreamVersionsPipeline, cancellationToken: cancellationToken)
            .ForEachAsync(x => _headStreamVersions[x["_id"].AsString] = x["version"].AsInt64, cancellationToken);

        await Task.WhenAll(duplicatesTask, versionsTask).ConfigureAwait(false);

        logger.LogDebug(
            "Prefetch complete: {DuplicateCount} duplicate(s) found, {StreamCount} stream version(s) loaded",
            _duplicateCommitIds.Count,
            _headStreamVersions.Count);
    }
}