using System.Diagnostics;
using System.Threading.Channels;
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
    private int _disposed;

    public BatchingAppender(
        NpgsqlDataSource dataSource,
        SchemaObjectNames names,
        PostgresEventStoreOptions options,
        ILogger? logger)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.AppendQueueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.AppendMaxBatchSize);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.AppendBatchingDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(options.AppendBatchingDelayMinCount);

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
        var batch = new List<AppendRequest>(_maxBatchSize);

        try
        {
            while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (batch.Count < _maxBatchSize && _channel.Reader.TryRead(out var request))
                    batch.Add(request);

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
                            while (batch.Count < _maxBatchSize && _channel.Reader.TryRead(out var request))
                                batch.Add(request);
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

    private async Task ProcessBatchAsync(List<AppendRequest> batch, CancellationToken ct)
    {
        try
        {
            await _command.ExecuteAsync(batch, ct).ConfigureAwait(false);
        }
        catch (NpgsqlException ex) when (ex.IsTransient && !ct.IsCancellationRequested)
        {
            // Retrying is safe because commit IDs make already-committed requests report as duplicates.
            _logger?.AppendBatchRetrying(ex, batch.Count);
            await _command.ExecuteAsync(batch, ct).ConfigureAwait(false);
        }
    }

    private static void FaultAll(List<AppendRequest> batch, Exception exception)
    {
        foreach (var request in batch)
            request.TryComplete(exception);
    }

    private static void DrainWithFault(ChannelReader<AppendRequest> reader, Exception exception)
    {
        while (reader.TryRead(out var request))
            request.TryComplete(exception);
    }
}