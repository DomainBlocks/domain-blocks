using DomainBlocks.EventStore.MongoDB.Schema;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal partial class EventLogWriter
{
    private static readonly BsonDocument GroupByStreamStage = new("$group", new BsonDocument
    {
        { "_id", $"${EventLogEntry.FieldNames.StreamId}" },
        { "version", new BsonDocument("$max", $"${EventLogEntry.FieldNames.StreamVersion}") }
    });

    private readonly BsonDocument _visibilityFilter = epochStartPosition.HasValue
        ? new BsonDocument("$or", new BsonArray
        {
            new BsonDocument(EventLogEntry.FieldNames.Epoch, epoch),
            new BsonDocument("_id", new BsonDocument("$lte", epochStartPosition.Value))
        })
        : new BsonDocument(EventLogEntry.FieldNames.Epoch, epoch);

    private readonly HashSet<BsonValue> _prepareDedup = [];
    private readonly BsonArray _prepareCommitIds = [];
    private readonly BsonArray _prepareStreamIds = [];

    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        _prepareDedup.Clear();
        _prepareCommitIds.Clear();
        _prepareStreamIds.Clear();

        foreach (var request in _requests)
        {
            var commitId = request[AppendRequest.FieldNames.CommitId];
            var streamId = request[AppendRequest.FieldNames.StreamId];

            if (_prepareDedup.Add(commitId))
                _prepareCommitIds.Add(commitId);

            if (_prepareDedup.Add(streamId))
                _prepareStreamIds.Add(streamId);
        }

        var commitIdFilter = new BsonDocument("$and", new BsonArray
        {
            new BsonDocument(EventLogEntry.FieldNames.CommitId, new BsonDocument("$in", _prepareCommitIds)),
            _visibilityFilter
        });

        var maxStreamVersionsPipeline = new[]
        {
            new BsonDocument("$match",
                new BsonDocument("$and", new BsonArray
                {
                    new BsonDocument(EventLogEntry.FieldNames.StreamId, new BsonDocument("$in", _prepareStreamIds)),
                    _visibilityFilter
                })),

            GroupByStreamStage
        };

        var duplicatesTask = eventLog
            .Distinct<BsonValue>(
                EventLogEntry.FieldNames.CommitId,
                commitIdFilter,
                cancellationToken: cancellationToken)
            .ForEachAsync(x => _duplicateCommitIds.Add(x.AsGuid), cancellationToken);

        var versionsTask = eventLog
            .Aggregate<BsonDocument>(maxStreamVersionsPipeline, cancellationToken: cancellationToken)
            .ForEachAsync(x => _headStreamVersions[x["_id"].AsString] = x["version"].AsInt64, cancellationToken);

        await Task.WhenAll(duplicatesTask, versionsTask).ConfigureAwait(false);

        logger.LogDebug(
            "Prepare complete: Found {DuplicateCount} duplicate(s), loaded {StreamCount} stream version(s)",
            _duplicateCommitIds.Count,
            _headStreamVersions.Count);
    }
}