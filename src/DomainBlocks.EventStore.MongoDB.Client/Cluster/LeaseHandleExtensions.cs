using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Cluster;

public static class LeaseHandleExtensions
{
    public static Task<bool> TryAdvanceCommitPositionAsync(
        this ILeaseHandle<LogLeaseState> handle,
        long amount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        return handle.TryUpdateStateAsync(x => x.Inc(s => s.CommitPosition, amount), cancellationToken);
    }
}