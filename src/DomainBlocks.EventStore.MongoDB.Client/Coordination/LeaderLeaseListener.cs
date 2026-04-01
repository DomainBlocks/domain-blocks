using DomainBlocks.EventStore.MongoDB.Client.Schema;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class LeaderLeaseListener(
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

        var appender = new EventLogAppender(
            eventLog,
            handle.Snapshot.Epoch,
            leaseState.CommitPosition,
            loggerFactory.CreateLogger<EventLogAppender>());

        _session = new LeaderSession(
            requests,
            appender,
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