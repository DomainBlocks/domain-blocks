using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender.Events;

public sealed class LeaseUpdateObserved(ILeaseSnapshot snapshot) : IAppenderEvent
{
    public ILeaseSnapshot Snapshot { get; } = snapshot;
}