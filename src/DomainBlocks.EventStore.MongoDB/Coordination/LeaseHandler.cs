using System.Threading.Channels;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public sealed class LeaseHandler(
    IMongoCollection<BsonDocument> requests,
    IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject,
    IMongoCollection<BsonDocument> eventLog,
    int queueCapacity,
    int batchSize,
    ILoggerFactory loggerFactory) :
    ILeaseHandler
{
    private LeaderSession? _session;
    private RequestFeeder? _requestFeeder;

    public Task HandleLeaseAcquiredAsync(Lease lease, CancellationToken cancellationToken)
    {
        var eventLogWriter = new EventLogWriter(
            eventLog,
            lease.Epoch,
            lease.CommitPosition,
            loggerFactory.CreateLogger<EventLogWriter>());

        var requestChannel = Channel.CreateBounded<BsonDocument>(
            new BoundedChannelOptions(queueCapacity)
            {
                SingleWriter = true,
                SingleReader = true
            });

        _session = new LeaderSession(
            lease,
            requestChannel.Reader,
            eventLogWriter,
            batchSize,
            loggerFactory.CreateLogger<LeaderSession>());

        _requestFeeder = new RequestFeeder(
            requests,
            changeStreamSubject,
            queueCapacity,
            batchSize,
            loggerFactory.CreateLogger<RequestFeeder>());

        _requestFeeder.Start(requestChannel.Writer);

        return Task.CompletedTask;
    }

    public async Task HandleLeaseLostAsync(LeaseLostInfo lostInfo, CancellationToken cancellationToken)
    {
        if (_session is not null)
            await _session.DisposeAsync().ConfigureAwait(false);

        if (_requestFeeder is not null)
            await _requestFeeder.DisposeAsync().ConfigureAwait(false);
    }
}