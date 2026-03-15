using DomainBlocks.EventStore.MongoDB.Client.Appender;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class EventAppenderFactory(CollectionNamespace appendRequestsNamespace, ILoggerFactory loggerFactory) :
    ILeaderWorkerFactory
{
    public ILeaderWorker Create(ILeaseHandle<LeaseState> handle)
    {
        var logger = loggerFactory.CreateLogger<EventAppender>();
        return new EventAppender(handle, appendRequestsNamespace, logger);
    }
}