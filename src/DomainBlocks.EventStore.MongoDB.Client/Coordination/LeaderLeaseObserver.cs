using DomainBlocks.EventStore.MongoDB.Client.Schema;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class LeaderLeaseObserver(
    IMongoCollection<BsonDocument> requests,
    IMongoCollection<BsonDocument> eventLog,
    IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject,
    ILoggerFactory loggerFactory) :
    ILeaseObserver
{
    private LeaderSession? _session;

    private readonly TaskCompletionSource _livelinessTcs = new();

    public Task Liveliness => _livelinessTcs.Task;

    public async Task OnLeaseAcquiredAsync(ILeaseHandle<LeaseState> handle, CancellationToken ct)
    {
        var leaseState = BsonSerializer.Deserialize<LeaseState>(handle.Snapshot.State);

        var appender = new EventLogAppender(
            eventLog,
            handle.Snapshot.Epoch,
            leaseState.CommitPosition,
            loggerFactory.CreateLogger<EventLogAppender>());

        _session = new LeaderSession(
            handle,
            appender,
            requests,
            changeStreamSubject,
            loggerFactory);

        _session.Start();

        await _session.Liveliness.WaitAsync(ct).ConfigureAwait(false);

        _livelinessTcs.TrySetResult();
    }

    public async Task OnLeaseLostAsync(LeaseClaim claim, LeaseLostInfo? info, CancellationToken ct)
    {
        if (_session is not null)
            await _session.DisposeAsync();
    }
}