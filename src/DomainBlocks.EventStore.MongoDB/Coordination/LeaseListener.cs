using DomainBlocks.EventStore.MongoDB.Schema;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public sealed class LeaseListener(
    IMongoCollection<BsonDocument> requests,
    IMongoCollection<BsonDocument> eventLog,
    IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject,
    LeaderOptions options,
    ILoggerFactory loggerFactory) :
    ILeaseListener
{
    private LeaderSession? _session;

    public Task OnLeaseAcquiredAsync(ILeaseHandle<LeaseState> handle, CancellationToken ct)
    {
        var leaseState = BsonSerializer.Deserialize<LeaseState>(handle.Snapshot.State);

        var writer = new EventLogWriter(
            eventLog,
            handle.Snapshot.Epoch,
            leaseState.CommitPosition,
            loggerFactory.CreateLogger<EventLogWriter>());

        _session = new LeaderSession(
            requests,
            writer,
            handle,
            changeStreamSubject,
            options,
            loggerFactory);

        _session.Start();

        return Task.CompletedTask;
    }

    public async Task OnLeaseLostAsync(LeaseClaim claim, LeaseLostInfo? info, CancellationToken ct)
    {
        if (_session is not null)
            await _session.DisposeAsync();
    }
}