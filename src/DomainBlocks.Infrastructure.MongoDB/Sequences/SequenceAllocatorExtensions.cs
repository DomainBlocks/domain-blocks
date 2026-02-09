using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Sequences;

public static class SequenceAllocatorExtensions
{
    extension(ISequenceAllocator sequenceAllocator)
    {
        public async Task<long> AllocateNextAsync(string sequenceId, CancellationToken cancellationToken = default)
        {
            var range = await sequenceAllocator.AllocateNextRangeAsync(sequenceId, 1, cancellationToken);
            return range.Start;
        }

        public async Task<long> AllocateNextAsync(
            IClientSessionHandle session,
            string sequenceId,
            CancellationToken cancellationToken = default)
        {
            var range = await sequenceAllocator.AllocateNextRangeAsync(session, sequenceId, 1, cancellationToken);
            return range.Start;
        }
    }
}