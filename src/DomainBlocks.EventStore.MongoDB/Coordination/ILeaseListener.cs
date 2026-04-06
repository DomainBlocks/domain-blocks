using DomainBlocks.EventStore.MongoDB.Schema;
using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public interface ILeaseListener
{
    Task OnLeaseAcquiredAsync(ILeaseHandle<LeaseState> leaseHandle, CancellationToken cancellationToken);

    Task OnLeaseLostAsync(LeaseClaim leaseClaim, LeaseLostInfo? leaseLostInfo, CancellationToken cancellationToken);
}