using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender.Events;

public sealed class LeaderLeaseAcquired(ILeaseHandle<LeaseState> handle) : IAppenderEvent
{
    public ILeaseHandle<LeaseState> Handle { get; } = handle;
}