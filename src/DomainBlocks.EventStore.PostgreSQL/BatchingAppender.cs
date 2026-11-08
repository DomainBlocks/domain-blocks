using System.Diagnostics;
using System.Threading.Channels;
using DomainBlocks.EventStore.Internal;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Queues append requests and commits each batch in one round trip. Every append serializes on the sequence row, so a
/// batch lets one lock acquisition and one round trip serve many requests.
/// </summary>
internal sealed class BatchingAppender : IAppender
{
    private readonly AppendBatchCommand _command;
    private readonly int _maxBatchSize;
    private readonly TimeSpan _batchingDelay;
    private readonly int _batchingDelayMinCount;
    private readonly ILogger? _logger;
    private readonly Channel<AppendRequest> _channel;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly Task _runAppendLoopTask;
    private readonly HashSet<Guid> _batchCommitIds = [];
    private int _disposed;

    public BatchingAppender(
        NpgsqlDataSource dataSource,
        SchemaObjectNames names,
        PostgresEventStoreOptions options,
        ILogger? logger)
    {
        CancellationTokenSource.ThrowIfDelayOutOfRange(options.AppendBatchingDelay);

        _command = new AppendBatchCommand(dataSource, names);
        _maxBatchSize = options.AppendMaxBatchSize;
        _batchingDelay = options.AppendBatchingDelay;
        _batchingDelayMinCount = options.AppendBatchingDelayMinCount;
        _logger = logger;

        _channel = Channel.CreateBounded<AppendRequest>(new BoundedChannelOptions(options.AppendQueueCapacity)
        {
            SingleWriter = false,
            SingleReader = true
        });

        _runAppendLoopTask = RunAppendLoopAsync(_stopCts.Token);
    }

    public async Task AppendAsync(AppendRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        CancellationTokenSource.ThrowIfDelayOutOfRange(timeout);

        using var linkedTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedTimeoutCts.CancelAfter(timeout);

        try
        {
            await _channel.Writer.WriteAsync(request, linkedTimeoutCts.Token).ConfigureAwait(false);
            await request.Completion.WaitAsync(linkedTimeoutCts.Token).ConfigureAwait(false);
        }
        catch (ChannelClosedException ex)
        {
            throw new ObjectDisposedException(nameof(BatchingAppender), ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (linkedTimeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Append operation did not complete within {timeout}.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _channel.Writer.TryComplete();
        await _stopCts.CancelAsync().ConfigureAwait(false);
        await _runAppendLoopTask.ConfigureAwait(false);
        _stopCts.Dispose();
        _command.Dispose();
    }

    private async Task RunAppendLoopAsync(CancellationToken ct)
    {
        // The batch grows as it fills, since the maximum batch size can be far larger than any real batch.
        var batch = new List<AppendRequest>();

        try
        {
            while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                TakeQueued(batch);

                // Waiting briefly for more requests trades latency for larger batches.
                if (_batchingDelay > TimeSpan.Zero &&
                    batch.Count >= _batchingDelayMinCount &&
                    batch.Count < _maxBatchSize)
                {
                    using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    delayCts.CancelAfter(_batchingDelay);

                    try
                    {
                        while (batch.Count < _maxBatchSize &&
                               await _channel.Reader.WaitToReadAsync(delayCts.Token).ConfigureAwait(false))
                        {
                            TakeQueued(batch);
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // The delay elapsed; commit what we have.
                    }
                }

                if (batch.Count == 0)
                    continue;

                try
                {
                    var start = Stopwatch.GetTimestamp();

                    await ProcessBatchAsync(batch, ct).ConfigureAwait(false);

                    _logger?.AppendBatchProcessed(
                        batch.Count,
                        _maxBatchSize,
                        _channel.Reader.Count,
                        Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // Let the outer catch handle the graceful stop.
                }
                catch (Exception ex)
                {
                    _logger?.AppendBatchFailed(ex, batch.Count);
                    FaultAll(batch, ex);
                }

                batch.Clear();
                _batchCommitIds.Clear();
            }
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            _logger?.AppendLoopStopped();
            _channel.Writer.TryComplete();
            FaultAll(batch, ex);
            DrainWithFault(_channel.Reader, ex);
        }
        catch (Exception ex)
        {
            _logger?.AppendLoopFailed(ex);
            _channel.Writer.TryComplete(ex);
            FaultAll(batch, ex);
            DrainWithFault(_channel.Reader, ex);
        }
    }

    // A repeated commit ID is rejected rather than sent, because the batch would report it as a committed duplicate
    // even when the first append with that ID is rejected.
    private void TakeQueued(List<AppendRequest> batch)
    {
        while (batch.Count < _maxBatchSize && _channel.Reader.TryRead(out var request))
        {
            if (_batchCommitIds.Add(request.CommitId))
                batch.Add(request);
            else
                request.TryComplete(AppendError.CommitIdInProgress(request.CommitId));
        }
    }

    private async Task ProcessBatchAsync(List<AppendRequest> batch, CancellationToken ct)
    {
        try
        {
            await _command.ExecuteAsync(batch, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (PostgresTransientErrors.IsTransient(ex) && !ct.IsCancellationRequested)
        {
            // Retrying is safe because commit IDs make already-committed requests report as duplicates. A batch of
            // duplicates still locks the sequence row, which writes WAL, so its commit waits for the synchronous
            // standbys, and once they confirm it they have every earlier commit too.
            _logger?.AppendBatchRetrying(ex, batch.Count);
            await _command.ExecuteAsync(batch, ct).ConfigureAwait(false);
        }
    }

    private static void FaultAll(List<AppendRequest> batch, Exception exception)
    {
        // A batch whose commit wasn't confirmed may have been written, so its callers must be able to tell that apart
        // from a failed append.
        foreach (var request in batch)
        {
            request.TryComplete(exception is CommitNotConfirmedException
                ? new AppendNotConfirmedException(request.StreamId, exception)
                : exception);
        }
    }

    private static void DrainWithFault(ChannelReader<AppendRequest> reader, Exception exception)
    {
        while (reader.TryRead(out var request))
            request.TryComplete(exception);
    }
}