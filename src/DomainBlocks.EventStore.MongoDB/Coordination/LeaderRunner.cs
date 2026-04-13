using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal sealed class LeaderRunner(
    RequestFeeder requestFeeder,
    IMongoCollection<BsonDocument> eventLog,
    int queueCapacity,
    int batchSize,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger<LeaderRunner> _logger = loggerFactory.CreateLogger<LeaderRunner>();

    public async Task RunAsync(Lease lease, CancellationToken cancellationToken)
    {
        var requestChannel = Channel.CreateBounded<BsonDocument>(
            new BoundedChannelOptions(queueCapacity)
            {
                SingleWriter = true,
                SingleReader = true
            });

        var eventLogWriter = new EventLogWriter(
            eventLog,
            lease.Epoch,
            lease.CommitPosition,
            loggerFactory.CreateLogger<EventLogWriter>());

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lease.LeaseLostToken);

        var requestFeederTask = requestFeeder.RunAsync(requestChannel.Writer, linkedCts.Token);

        try
        {
            await ProcessRequestsAsync(lease, requestChannel, eventLogWriter, linkedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            await linkedCts.CancelAsync().ConfigureAwait(false);
            await requestFeederTask.ConfigureAwait(false);
        }
    }

    private async Task ProcessRequestsAsync(
        Lease lease,
        ChannelReader<BsonDocument> requestReader,
        EventLogWriter eventLogWriter,
        CancellationToken ct)
    {
        var batch = new List<BsonDocument>(batchSize);
        var advanceTask = Task.FromResult(true);

        try
        {
            while (await requestReader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                batch.Clear();

                while (batch.Count < batchSize && requestReader.TryRead(out var request))
                    batch.Add(request);

                if (batch.Count == 0)
                    continue;

                // Fire prepare immediately - runs concurrently with the previous advance.
                eventLogWriter.Prepare(batch, ct);

                // Await the previous advance before writing.
                if (!await advanceTask.ConfigureAwait(false))
                    return;

                _logger.LogDebug("Preparing {BatchSize} commit(s)", batch.Count);

                var result = await eventLogWriter.FlushAsync(ct).ConfigureAwait(false);

                // Advance without awaiting.
                advanceTask = TryAdvanceCommitPositionAsync(lease, result, ct);

                // If the queue is empty, advance immediately rather than waiting until the next batch.
                if (requestReader.Count == 0)
                {
                    if (!await advanceTask.ConfigureAwait(false))
                        return;

                    advanceTask = Task.FromResult(true);
                }
            }

            // Await the final advance after the channel drains naturally.
            await advanceTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error processing requests (epoch {Epoch})", lease.Epoch);
            throw;
        }
    }

    private async Task<bool> TryAdvanceCommitPositionAsync(
        Lease lease,
        EventLogWriteResult result,
        CancellationToken ct)
    {
        if (result == EventLogWriteResult.Empty)
            return true;

        var success = await lease.TryAdvanceCommitPositionAsync(result.Count, ct).ConfigureAwait(false);
        if (!success)
            _logger.LogWarning("Failed to advance commit position by {Count}", result.Count);

        return success;
    }
}