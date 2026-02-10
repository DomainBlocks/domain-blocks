using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Sequences;

public interface ISequenceStore
{
    Task<SequenceRange> NextRangeAsync(string sequenceId, long count, CancellationToken cancellationToken = default);

    Task<SequenceRange> NextRangeAsync(
        IClientSessionHandle session,
        string sequenceId,
        long count,
        CancellationToken cancellationToken = default);
}