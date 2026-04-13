using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal sealed class RequestFeeder(
    IMongoCollection<BsonDocument> requests,
    IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject,
    int queueCapacity,
    int batchSize,
    ILogger<RequestFeeder> logger)
{
    private readonly BoundedChannelOptions _channelOptions = new(queueCapacity)
    {
        SingleWriter = true,
        SingleReader = true
    };

    public async Task RunAsync(ChannelWriter<BsonDocument> output, CancellationToken cancellationToken)
    {
        var catchUpChannel = Channel.CreateBounded<BsonDocument>(_channelOptions);
        var liveChannel = Channel.CreateBounded<BsonDocument>(_channelOptions);
        var liveObserver = new LiveObserver(liveChannel.Writer, requests.CollectionNamespace);

        // Attach before catch-up so live inserts are buffered immediately.
        using (changeStreamSubject.Attach(liveObserver))
        {
            await Task
                .WhenAll(
                    CatchUpAsync(catchUpChannel.Writer, cancellationToken),
                    PumpAsync(catchUpChannel.Reader, liveChannel.Reader, output, cancellationToken))
                .ConfigureAwait(false);
        }
    }

    private async Task CatchUpAsync(ChannelWriter<BsonDocument> writer, CancellationToken ct)
    {
        logger.LogInformation("Catch-up phase starting");

        var sort = Builders<BsonDocument>.Sort.Ascending("_id");
        BsonValue? lastSeenId = null;

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var filter = lastSeenId is not null
                    ? Builders<BsonDocument>.Filter.Gt("_id", lastSeenId)
                    : FilterDefinition<BsonDocument>.Empty;

                var batch = await requests
                    .Find(filter)
                    .Sort(sort)
                    .Limit(batchSize)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                if (batch.Count == 0)
                    break;

                foreach (var request in batch)
                {
                    lastSeenId = request["_id"];
                    await writer.WriteAsync(request, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogDebug("Catch-up cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Catch-up failed");
            writer.TryComplete(ex);
            return;
        }

        writer.TryComplete();
    }

    private async Task PumpAsync(
        ChannelReader<BsonDocument> catchUpReader,
        ChannelReader<BsonDocument> liveReader,
        ChannelWriter<BsonDocument> output,
        CancellationToken ct)
    {
        try
        {
            // Phase 1: drain catch-up fully before processing any live items.
            await foreach (var item in catchUpReader.ReadAllAsync(ct).ConfigureAwait(false))
                await output.WriteAsync(item, ct).ConfigureAwait(false);

            logger.LogInformation("Switching to live mode");

            // Phase 2: drain live indefinitely until canceled or channel completed.
            await foreach (var item in liveReader.ReadAllAsync(ct).ConfigureAwait(false))
                await output.WriteAsync(item, ct).ConfigureAwait(false);

            output.TryComplete();
        }
        catch (Exception ex)
        {
            output.TryComplete(ex);
        }
    }

    private sealed class LiveObserver(ChannelWriter<BsonDocument> writer, CollectionNamespace ns) :
        IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>
    {
        public async ValueTask OnNextAsync(
            ChangeStreamDocument<BsonDocument> change,
            CancellationToken cancellationToken)
        {
            if (!change.CollectionNamespace.Equals(ns) || change.OperationType is not ChangeStreamOperationType.Insert)
                return;

            await writer.WriteAsync(change.FullDocument, cancellationToken).ConfigureAwait(false);
        }
    }
}