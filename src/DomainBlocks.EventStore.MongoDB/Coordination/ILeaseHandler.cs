namespace DomainBlocks.EventStore.MongoDB.Coordination;

public interface ILeaseHandler
{
    Task HandleLeaseAcquiredAsync(Lease lease, CancellationToken cancellationToken);

    Task HandleLeaseLostAsync(LeaseLostInfo lostInfo, CancellationToken cancellationToken);
}