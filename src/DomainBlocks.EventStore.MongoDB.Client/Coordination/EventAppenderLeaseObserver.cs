using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class EventAppenderLeaseObserver(
    IMongoCollection<AppendRequest> requests,
    IMongoCollection<EventLogEntry> eventLog,
    IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject,
    ILoggerFactory loggerFactory) :
    ILeaseObserver
{
    private EventAppenderSession? _session;

    public Task OnLeaseAcquiredAsync(ILeaseHandle<LeaseState> handle, CancellationToken ct)
    {
        var leaseState = BsonSerializer.Deserialize<LeaseState>(handle.CurrentSnapshot.State);

        var appender = new EventAppender(
            eventLog,
            handle.CurrentSnapshot.Epoch,
            leaseState.CommitPosition,
            loggerFactory.CreateLogger<EventAppender>());

        var requestCompleter = new AppendRequestCompleter(
            requests,
            loggerFactory.CreateLogger<AppendRequestCompleter>());

        _session = new EventAppenderSession(
            handle,
            leaseState.CommitPosition,
            appender,
            requests,
            eventLog,
            changeStreamSubject,
            requestCompleter,
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