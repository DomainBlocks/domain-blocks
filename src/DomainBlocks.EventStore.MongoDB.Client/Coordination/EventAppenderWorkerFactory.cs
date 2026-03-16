using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class EventAppenderWorkerFactory(
    IMongoCollection<AppendRequest> appendRequests,
    IMongoCollection<EventLogEntry> eventLog,
    ILoggerFactory loggerFactory) :
    ILeaderWorkerFactory
{
    public ILeaderWorker Create(ILeaseHandle<LeaseState> handle)
    {
        var logger = loggerFactory.CreateLogger<EventAppenderWorker>();
        return new EventAppenderWorker(handle, appendRequests, eventLog, logger);
    }
}