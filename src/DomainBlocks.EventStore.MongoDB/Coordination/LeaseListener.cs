using System.Threading.Channels;
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
    private RequestPump? _requestPump;

    public Task OnLeaseAcquiredAsync(ILeaseHandle<LeaseState> leaseHandle, CancellationToken ct)
    {
        var leaseState = BsonSerializer.Deserialize<LeaseState>(leaseHandle.Snapshot.State);

        var eventLogWriter = new EventLogWriter(
            eventLog,
            leaseHandle.Snapshot.Epoch,
            leaseState.CommitPosition,
            loggerFactory.CreateLogger<EventLogWriter>());

        var requestChannel = Channel.CreateBounded<BsonDocument>(
            new BoundedChannelOptions(options.QueueCapacity)
            {
                SingleWriter = true,
                SingleReader = true
            });

        _session = new LeaderSession(
            leaseHandle,
            requestChannel.Reader,
            eventLogWriter,
            options.BatchSize,
            loggerFactory.CreateLogger<LeaderSession>());

        _requestPump = new RequestPump(
            requests,
            changeStreamSubject,
            options,
            loggerFactory.CreateLogger<RequestPump>());

        _requestPump.Start(requestChannel.Writer);

        return Task.CompletedTask;
    }

    public async Task OnLeaseLostAsync(LeaseClaim claim, LeaseLostInfo? info, CancellationToken ct)
    {
        if (_session is not null)
            await _session.DisposeAsync();

        if (_requestPump is not null)
            await _requestPump.DisposeAsync();
    }
}