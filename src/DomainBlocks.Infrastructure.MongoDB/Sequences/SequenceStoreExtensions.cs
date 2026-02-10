using MongoDB.Driver;

namespace DomainBlocks.Infrastructure.MongoDB.Sequences;

public static class SequenceStoreExtensions
{
    extension(ISequenceStore sequenceStore)
    {
        public async Task<long> NextAsync(string sequenceId, CancellationToken cancellationToken = default)
        {
            var range = await sequenceStore.NextRangeAsync(sequenceId, 1, cancellationToken);
            return range.Start;
        }

        public async Task<long> NextAsync(
            IClientSessionHandle session,
            string sequenceId,
            CancellationToken cancellationToken = default)
        {
            var range = await sequenceStore.NextRangeAsync(session, sequenceId, 1, cancellationToken);
            return range.Start;
        }
    }
}