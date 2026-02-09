using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Sequences;

public interface ISequenceAllocator
{
    Task<SequenceRange> AllocateNextRangeAsync(
        string sequenceId,
        long count,
        CancellationToken cancellationToken = default);

    Task<SequenceRange> AllocateNextRangeAsync(
        IClientSessionHandle session,
        string sequenceId,
        long count,
        CancellationToken cancellationToken = default);
}