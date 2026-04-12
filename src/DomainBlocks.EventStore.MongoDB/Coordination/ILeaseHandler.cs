namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal interface ILeaseHandler
{
    Task HandleLeaseAcquiredAsync(Lease lease, CancellationToken cancellationToken);

    Task HandleLeaseLostAsync(LeaseLostInfo lostInfo, CancellationToken cancellationToken);
}