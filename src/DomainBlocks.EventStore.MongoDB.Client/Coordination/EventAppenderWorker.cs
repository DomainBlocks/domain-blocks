using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class EventAppenderWorker : ILeaderWorker
{
    private readonly ILeaseHandle<LeaseState> _handle;
    private readonly IMongoCollection<AppendRequest> _appendRequests;
    private readonly IMongoCollection<EventLogEntry> _eventLog;
    private readonly ILogger<EventAppenderWorker> _logger;
    private readonly Channel<ChangeStreamDocument<BsonDocument>> _channel;
    private readonly CancellationTokenSource _stopCts;
    private long _nextPosition;
    private Task? _runTask;

    public EventAppenderWorker(
        ILeaseHandle<LeaseState> handle,
        IMongoCollection<AppendRequest> appendRequests,
        IMongoCollection<EventLogEntry> eventLog,
        ILogger<EventAppenderWorker> logger)
    {
        _handle = handle;
        _appendRequests = appendRequests;
        _eventLog = eventLog;
        _logger = logger;

        var channelOptions = new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        };

        _channel = Channel.CreateUnbounded<ChangeStreamDocument<BsonDocument>>(channelOptions);
        _stopCts = CancellationTokenSource.CreateLinkedTokenSource(handle.LeaseLostToken);

        var leaseState = BsonSerializer.Deserialize<LeaseState>(handle.CurrentSnapshot.State);
        _nextPosition = leaseState.CommitPosition.HasValue ? leaseState.CommitPosition.Value + 1 : 0;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _runTask = RunAsync();
        return Task.CompletedTask;
    }

    public ValueTask OnNextAsync(ChangeStreamDocument<BsonDocument> change, CancellationToken cancellationToken)
    {
        if (!change.CollectionNamespace.Equals(_appendRequests.CollectionNamespace))
            return ValueTask.CompletedTask;

        var id = change.DocumentKey["_id"];
        if (!id.IsGuid)
        {
            _logger.LogWarning("Append request ignored; _id '{Id}' is not a GUID value", id);
            return ValueTask.CompletedTask;
        }

        return _channel.Writer.WriteAsync(change, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _logger.LogInformation("DisposeAsync");
        return ValueTask.CompletedTask;
    }

    private async Task RunAsync()
    {
        await CatchUpAsync().ConfigureAwait(false);
        await RunLiveAsync().ConfigureAwait(false);
    }

    private Task CatchUpAsync()
    {
        return Task.CompletedTask;
    }

    private Task RunLiveAsync()
    {
        return Task.CompletedTask;
    }

    private Task AppendEventsAsync(IEnumerable<AppendRequest> requests)
    {
        return Task.CompletedTask;
    }
}