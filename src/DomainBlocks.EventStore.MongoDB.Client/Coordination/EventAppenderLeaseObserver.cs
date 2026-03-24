using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class EventAppenderLeaseObserver(
    IMongoCollection<BsonDocument> requests,
    IMongoCollection<BsonDocument> eventLog,
    IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject,
    ILoggerFactory loggerFactory) :
    ILeaseObserver
{
    private EventAppenderSession? _session;

    private readonly TaskCompletionSource _livelinessTcs = new();

    public Task Liveliness => _livelinessTcs.Task;

    public async Task OnLeaseAcquiredAsync(ILeaseHandle<LeaseState> handle, CancellationToken ct)
    {
        var leaseState = BsonSerializer.Deserialize<LeaseState>(handle.CurrentSnapshot.State);

        var appender = new FastEventAppender(
            eventLog,
            handle.CurrentSnapshot.Epoch,
            leaseState.CommitPosition,
            loggerFactory.CreateLogger<FastEventAppender>());

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

        await _session.Liveliness.WaitAsync(ct).ConfigureAwait(false);

        _livelinessTcs.TrySetResult();
    }

    public async Task OnLeaseLostAsync(LeaseClaim claim, LeaseLostInfo? info, CancellationToken ct)
    {
        if (_session is not null)
            await _session.DisposeAsync();
    }
}