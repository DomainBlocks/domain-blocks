using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Sequences;

public sealed class SequenceStore(IMongoCollection<BsonDocument> sequences) : ISequenceStore
{
    private const string NextValueFieldName = "nextValue";

    private static readonly FindOneAndUpdateOptions<BsonDocument> FindOneAndUpdateOptions = new()
    {
        IsUpsert = true,
        ReturnDocument = ReturnDocument.Before
    };

    public Task<SequenceRange> NextRangeAsync(
        string sequenceId,
        long count,
        CancellationToken cancellationToken = default)
    {
        return AllocateNextRangeCoreAsync(sequenceId, count, cancellationToken: cancellationToken);
    }

    public Task<SequenceRange> NextRangeAsync(
        IClientSessionHandle session,
        string sequenceId,
        long count,
        CancellationToken cancellationToken = default)
    {
        return AllocateNextRangeCoreAsync(sequenceId, count, session, cancellationToken);
    }

    private async Task<SequenceRange> AllocateNextRangeCoreAsync(
        string sequenceId,
        long count,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be positive.");

        var filter = new BsonDocument("_id", sequenceId);
        var update = new BsonDocument("$inc", new BsonDocument(NextValueFieldName, count));

        BsonDocument? previous;

        if (session is null)
        {
            previous = await sequences
                .FindOneAndUpdateAsync(filter, update, FindOneAndUpdateOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            previous = await sequences
                .FindOneAndUpdateAsync(session, filter, update, FindOneAndUpdateOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        var start = previous?[NextValueFieldName].ToInt64() ?? 0L;

        return new SequenceRange(start, count);
    }
}