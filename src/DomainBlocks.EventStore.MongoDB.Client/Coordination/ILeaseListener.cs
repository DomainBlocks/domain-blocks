using DomainBlocks.EventStore.MongoDB.Client.Schema;
using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface ILeaseListener
{
    Task OnLeaseAcquiredAsync(ILeaseHandle<LeaseState> handle, CancellationToken cancellationToken);

    Task OnLeaseLostAsync(LeaseClaim leaseClaim, LeaseLostInfo? leaseLostInfo, CancellationToken cancellationToken);
}