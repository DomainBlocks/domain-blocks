using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender.LeaderElection;

public interface ILocalLeaseObserver
{
    Task OnLocalLeaseAcquired(ILeaseHandle<LeaseState> handle, CancellationToken cancellationToken = default);

    Task OnLocalLeaseLost(
        LeaseClaim leaseClaim,
        LeaseLostInfo leaseLostInfo,
        CancellationToken cancellationToken = default);
}