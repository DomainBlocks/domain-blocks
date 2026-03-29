using DomainBlocks.EventStore.MongoDB.Client.Schema;
using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public static class LeaseHandleExtensions
{
    extension(ILeaseHandle<LeaseState> handle)
    {
        public Task<bool> TryAdvanceCommitPositionAsync(long count, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

            return handle.TryUpdateStateAsync(x => x.Inc(s => s.CommitPosition, count), cancellationToken);
        }
    }
}