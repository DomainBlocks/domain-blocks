using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public partial class EventAppender
{
    private static readonly BsonDocument GroupByStreamStage = new("$group", new BsonDocument
    {
        { "_id", $"${EventLogEntry.FieldNames.StreamId}" },
        { "version", new BsonDocument("$max", $"${EventLogEntry.FieldNames.StreamVersion}") }
    });

    private readonly BsonDocument _visibilityFilter = initialCommitPosition.HasValue
        ? new BsonDocument("$or", new BsonArray
        {
            new BsonDocument(EventLogEntry.FieldNames.Epoch, epoch),
            new BsonDocument("_id", new BsonDocument("$lte", initialCommitPosition.Value))
        })
        : new BsonDocument(EventLogEntry.FieldNames.Epoch, epoch);

    private readonly HashSet<BsonValue> _prefetchDedup = [];
    private readonly BsonArray _prefectCommitIds = [];
    private readonly BsonArray _prefetchStreamIds = [];

    private async Task PrefetchAsync(CancellationToken cancellationToken)
    {
        _prefetchDedup.Clear();
        _prefectCommitIds.Clear();
        _prefetchStreamIds.Clear();

        foreach (var request in _requests)
        {
            var commitId = request[AppendRequest.FieldNames.CommitId];
            var streamId = request[AppendRequest.FieldNames.StreamId];

            if (_prefetchDedup.Add(commitId))
                _prefectCommitIds.Add(commitId);

            if (_prefetchDedup.Add(streamId))
                _prefetchStreamIds.Add(streamId);
        }

        var commitIdFilter = new BsonDocument("$and", new BsonArray
        {
            new BsonDocument(EventLogEntry.FieldNames.CommitId, new BsonDocument("$in", _prefectCommitIds)),
            _visibilityFilter
        });

        var maxStreamVersionsPipeline = new[]
        {
            new BsonDocument("$match",
                new BsonDocument("$and", new BsonArray
                {
                    new BsonDocument(EventLogEntry.FieldNames.StreamId, new BsonDocument("$in", _prefetchStreamIds)),
                    _visibilityFilter
                })),

            GroupByStreamStage
        };

        var duplicatesTask = _eventLog
            .Distinct<BsonValue>(
                EventLogEntry.FieldNames.CommitId,
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