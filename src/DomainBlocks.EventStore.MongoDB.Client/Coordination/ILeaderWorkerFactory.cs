using DomainBlocks.EventStore.MongoDB.Client.Appender;
using DomainBlocks.Infrastructure.MongoDB.Leases;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public interface ILeaderWorkerFactory
{
    ILeaderWorker Create(ILeaseHandle<LeaseState> handle);
}