using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class PrefetchQuery
{
    private static readonly BsonDocument GroupByStreamStage = new("$group", new BsonDocument
    {
        { "_id", $"${EventLogEntry.FieldNames.StreamId}" },
        { "version", new BsonDocument("$max", $"${EventLogEntry.FieldNames.StreamVersion}") }
    });

    private readonly FilterDefinition<BsonDocument> _commitIdFilter;
    private readonly PipelineDefinition<BsonDocument, BsonDocument> _maxStreamVersionsPipeline;
    private readonly HashSet<BsonValue> _dedup = [];
    private readonly BsonArray _commitIdsBsonArray = [];
    private readonly BsonArray _streamIdsBsonArray = [];

    public PrefetchQuery(long epoch, long? commitPosition)
    {
        var visibilityFilter = commitPosition.HasValue
            ? new BsonDocument("$or", new BsonArray
            {
                new BsonDocument(EventLogEntry.FieldNames.Epoch, epoch),
                new BsonDocument("_id", new BsonDocument("$lte", commitPosition.Value))
            })
            : new BsonDocument(EventLogEntry.FieldNames.Epoch, epoch);

        _commitIdFilter = new BsonDocument("$and", new BsonArray
        {
            new BsonDocument(EventLogEntry.FieldNames.CommitId, new BsonDocument("$in", _commitIdsBsonArray)),
            visibilityFilter
        });

        _maxStreamVersionsPipeline = new[]
        {
            new BsonDocument("$match",
                new BsonDocument("$and", new BsonArray
                {
                    new BsonDocument(EventLogEntry.FieldNames.StreamId, new BsonDocument("$in", _streamIdsBsonArray)),
                    visibilityFilter
                })),

            GroupByStreamStage
        };
    }

    public async Task<PrefetchQueryResult> ExecuteAsync(
        IMongoCollection<BsonDocument> collection,
        IEnumerable<BsonDocument> requests,
        CancellationToken cancellationToken)
    {
        Prepare(requests);

        var duplicatesTask = collection
            .Distinct<BsonValue>(
                EventLogEntry.FieldNames.CommitId,
                _commitIdFilter,
                cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken);

        var versionsTask = collection
            .Aggregate(_maxStreamVersionsPipeline, cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken);

        await Task.WhenAll(duplicatesTask, versionsTask).ConfigureAwait(false);

        return new PrefetchQueryResult(
            await duplicatesTask.ConfigureAwait(false),
            await versionsTask.ConfigureAwait(false));
    }

    private void Prepare(IEnumerable<BsonDocument> requests)
    {
        ClearBuffers();

        foreach (var r in requests)
        {
            var commitId = r[AppendRequest.FieldNames.CommitId];
            if (_dedup.Add(commitId))
                _commitIdsBsonArray.Add(commitId);

            var streamId = r[AppendRequest.FieldNames.StreamId];
            if (_dedup.Add(streamId))
                _streamIdsBsonArray.Add(streamId);
        }
    }

    private void ClearBuffers()
    {
        _dedup.Clear();
        _commitIdsBsonArray.Clear();
        _streamIdsBsonArray.Clear();
    }
}