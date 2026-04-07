using DomainBlocks.EventStore.MongoDB.Schema;
using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public interface ILeaseHandler
{
    Task HandleLeaseAcquiredAsync(ILeaseHandle<LeaseState> leaseHandle, CancellationToken cancellationToken);

    Task HandleLeaseLostAsync(LeaseClaim leaseClaim, LeaseLostInfo? leaseLostInfo, CancellationToken cancellationToken);
}