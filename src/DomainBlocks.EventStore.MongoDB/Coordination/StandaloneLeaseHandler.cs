using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.Schema;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public sealed class StandaloneLeaseHandler(
    ChannelReader<BsonDocument> requestReader,
    IMongoCollection<BsonDocument> eventLog,
    CommitSubject commitSubject,
    int batchSize,
    ILoggerFactory loggerFactory) :
    ILeaseHandler
{
    private LeaderSession? _session;

    public Task HandleLeaseAcquiredAsync(ILeaseHandle<LeaseState> leaseHandle, CancellationToken cancellationToken)
    {
        var leaseState = BsonSerializer.Deserialize<LeaseState>(leaseHandle.Snapshot.State);

        var eventLogWriter = new EventLogWriter(
            eventLog,
            leaseHandle.Snapshot.Epoch,
            leaseState.CommitPosition,
            loggerFactory.CreateLogger<EventLogWriter>());

        _session = new LeaderSession(
            leaseHandle,
            requestReader,
            eventLogWriter,
            batchSize,
            loggerFactory.CreateLogger<LeaderSession>(),
            commitSubject);

        return Task.CompletedTask;
    }

    public async Task HandleLeaseLostAsync(
        LeaseClaim leaseClaim,
        LeaseLostInfo? leaseLostInfo,
        CancellationToken cancellationToken)
    {
        if (_session is not null)
            await _session.DisposeAsync().ConfigureAwait(false);
    }
}