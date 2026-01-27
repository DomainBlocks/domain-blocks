using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Appender;

internal sealed class SequenceAllocator(IMongoCollection<BsonDocument> sequences)
{
    private const string NextValueFieldName = "nextValue";

    private static readonly FindOneAndUpdateOptions<BsonDocument> FindOneAndUpdateOptions = new()
    {
        IsUpsert = true,
        ReturnDocument = ReturnDocument.Before
    };

    public async Task<SequenceAllocation> AllocateNextAsync(
        string sequenceId,
        long count,
        CancellationToken cancellationToken = default)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive.");

        var filter = new BsonDocument("_id", sequenceId);
        var update = new BsonDocument("$inc", new BsonDocument(NextValueFieldName, count));

        var previous = await sequences
            .FindOneAndUpdateAsync(filter, update, FindOneAndUpdateOptions, cancellationToken)
            .ConfigureAwait(false);

        var start = previous?[NextValueFieldName].ToInt64() ?? 0L;

        return new SequenceAllocation(start, count);
    }
}