namespace DomainBlocks.EventStore.MongoDB.Client;

public interface ISequenceAllocator
{
    Task<SequenceAllocation> AllocateNextAsync(
        string sequenceId,
        long count,
        CancellationToken cancellationToken = default);
}