using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

internal sealed class LeaderSession : IAsyncDisposable
{
    private readonly Lease _lease;
    private readonly ChannelReader<BsonDocument> _requestReader;
    private readonly IEventLogWriter _eventLogWriter;
    private readonly int _batchSize;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopCts;
    private readonly Task _runTask;

    public LeaderSession(
        Lease lease,
        ChannelReader<BsonDocument> requestReader,
        IEventLogWriter eventLogWriter,
        int batchSize,
        ILogger<LeaderSession> logger)
    {
        _lease = lease;
        _requestReader = requestReader;
        _eventLogWriter = eventLogWriter;
        _batchSize = batchSize;
        _logger = logger;
        _stopCts = CancellationTokenSource.CreateLinkedTokenSource(lease.LeaseLostToken);
        _runTask = RunAsync(_stopCts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        using (_stopCts)
        {
            _stopCts.CancelAfter(TimeSpan.FromSeconds(10));
            await _runTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await _stopCts.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var batch = new List<BsonDocument>(_batchSize);
        var advanceTask = Task.FromResult(true);

        while (await _requestReader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            batch.Clear();

            while (batch.Count < _batchSize && _requestReader.TryRead(out var request))
                batch.Add(request);

            if (batch.Count == 0)
                continue;

            // Fire prepare immediately - runs concurrently with the previous advance.
            _eventLogWriter.Prepare(batch, ct);

            // Await the previous advance before writing.
            if (!await advanceTask.ConfigureAwait(false))
                return;

            _logger.LogDebug("Preparing {BatchSize} commit(s)", batch.Count);

            var result = await _eventLogWriter.FlushAsync(ct).ConfigureAwait(false);

            // Advance without awaiting.
            advanceTask = TryAdvanceCommitPositionAsync(result, ct);

            // If the queue is empty, advance immediately rather than waiting until the next batch.
            if (_requestReader.Count == 0)
            {
                if (!await advanceTask.ConfigureAwait(false))
                    return;

                advanceTask = Task.FromResult(true);
            }
        }

        // Await the final advance after the channel drains naturally.
        await advanceTask.ConfigureAwait(false);
    }

    private async Task<bool> TryAdvanceCommitPositionAsync(EventLogWriteResult result, CancellationToken ct)
    {
        if (result == EventLogWriteResult.Empty)
            return true;

        var success = await _lease.TryAdvanceCommitPositionAsync(result.Count, ct).ConfigureAwait(false);

        if (!success)
            _logger.LogWarning("Failed to advance commit position by {Count}", result.Count);

        return success;
    }
}