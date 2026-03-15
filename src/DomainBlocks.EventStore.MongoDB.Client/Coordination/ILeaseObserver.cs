using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface ILeaseObserver
{
    Task OnLeaseAcquiredAsync(ILeaseHandle<LeaseState> handle, CancellationToken cancellationToken);

    Task OnLeaseLostAsync(LeaseClaim leaseClaim, LeaseLostInfo? leaseLostInfo, CancellationToken cancellationToken);
}