using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Strict;

internal sealed class SequenceStore(IMongoCollection<BsonDocument> sequencesCollection)
{
    private const string NextValueFieldName = "nextValue";

    public async Task<SequenceRange> NextRangeAsync(
        string sequenceId,
        long count,
        CancellationToken cancellationToken = default)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive.");

        var filter = new BsonDocument("_id", sequenceId);
        var update = new BsonDocument("$inc", new BsonDocument(NextValueFieldName, count));

        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.Before
        };

        var previous = await sequencesCollection
            .FindOneAndUpdateAsync(filter, update, options, cancellationToken)
            .ConfigureAwait(false);

        var start = previous?[NextValueFieldName].ToInt64() ?? 0L;

        return new SequenceRange(start, count);
    }
}