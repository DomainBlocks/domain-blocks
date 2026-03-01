using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender.LeaderElection;

public interface ILeaderLeaseObserver
{
    Task OnLeaderLeaseAcquired(ILeaseHandle<LeaseState> handle, CancellationToken cancellationToken = default);

    Task OnLeaderLeaseLost(
        LeaseClaim leaseClaim,
        LeaseLostInfo leaseLostInfo,
        CancellationToken cancellationToken = default);
}