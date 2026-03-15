using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.Client.Appender;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class EventAppender : ILeaderWorker
{
    private readonly ILeaseHandle<LeaseState> _handle;
    private readonly CollectionNamespace _appendRequestsNamespace;
    private readonly ILogger<EventAppender> _logger;
    private readonly Channel<ChangeStreamDocument<BsonDocument>> _channel;

    public EventAppender(
        ILeaseHandle<LeaseState> handle,
        CollectionNamespace appendRequestsNamespace,
        ILogger<EventAppender> logger)
    {
        _handle = handle;
        _appendRequestsNamespace = appendRequestsNamespace;
        _logger = logger;

        var channelOptions = new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        };

        _channel = Channel.CreateUnbounded<ChangeStreamDocument<BsonDocument>>(channelOptions);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("StartAsync");
        return Task.CompletedTask;
    }

    public ValueTask OnNextAsync(ChangeStreamDocument<BsonDocument> change, CancellationToken cancellationToken)
    {
        if (!change.CollectionNamespace.Equals(_appendRequestsNamespace))
            return ValueTask.CompletedTask;

        var id = change.DocumentKey["_id"];
        if (!id.IsGuid)
            return ValueTask.CompletedTask;

        return _channel.Writer.WriteAsync(change, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _logger.LogInformation("DisposeAsync");
        return ValueTask.CompletedTask;
    }
}