using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.MongoDB.Sequencing;

/// <inheritdoc/>
public sealed class MongoSequencedAppender<TDocument, TContext> : IMongoSequencedAppender<TDocument, TContext>
{
    private readonly MongoSequencedAppenderOptions _options;
    private readonly ILogger _logger;
    private readonly BatchProcessor<TContext> _batchProcessor;
    private readonly Channel<AppendRequest<TContext>> _channel;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly Task _runAppendLoopTask;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="MongoSequencedAppender{TDocument,TContext}"/>.
    /// </summary>
    /// <param name="mongoClient">The MongoDB client used by this appender.</param>
    /// <param name="binding">The sequence binding used by this appender.</param>
    /// <param name="appendPolicy">
    /// An optional policy for pre-commit logic and conflict resolution. Defaults to
    /// <see cref="DefaultSequencedAppenderPolicy{TContext}"/> if not provided.
    /// </param>
    /// <param name="options">Optional configuration.</param>
    /// <param name="logger">An optional logger. No logging occurs if not provided.</param>
    public MongoSequencedAppender(
        IMongoClient mongoClient,
        MongoSequenceBinding<TDocument> binding,
        IMongoSequencedAppenderPolicy<TContext>? appendPolicy = null,
        MongoSequencedAppenderOptions? options = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(mongoClient);
        ArgumentNullException.ThrowIfNull(binding);

        _options = options ?? new MongoSequencedAppenderOptions();
        _logger = logger ?? NullLogger.Instance;

        _batchProcessor = new BatchProcessor<TContext>(
            new MongoBatchCommitter<TDocument>(mongoClient, binding, _logger),
            appendPolicy ?? DefaultSequencedAppenderPolicy<TContext>.Shared,
            _options,
            _logger);

        _channel = Channel.CreateBounded<AppendRequest<TContext>>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            SingleWriter = false,
            SingleReader = true
        });

        _runAppendLoopTask = RunAppendLoopAsync(_stopCts.Token);
    }


    public async Task AppendAsync(
        IEnumerable<TDocument> documents,
        TContext context,
        Guid? requestId = null,
        AppendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var bsonDocuments = documents.Select(x => x.ToBsonDocument()).ToArray();
        if (bsonDocuments.Length == 0)
            return;

        options ??= new AppendOptions();
        var request = new AppendRequest<TContext>(bsonDocuments, context, requestId);

        using var linkedTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedTimeoutCts.CancelAfter(options.Timeout);

        try
        {
            await _channel.Writer.WriteAsync(request, linkedTimeoutCts.Token).ConfigureAwait(false);
            await request.Completion.WaitAsync(linkedTimeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller canceled.
            throw;
        }
        catch (OperationCanceledException) when (linkedTimeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Append operation did not complete within {options.Timeout}.");
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
    }

    private async Task RunAppendLoopAsync(CancellationToken ct)
    {
        var maxBatchSize = _options.MaxBatchSize;
        var batchingDelay = _options.BatchingDelay;
        var batchingDelayThreshold = _options.BatchingDelayMinCount;
        // The batch grows as it fills, since the maximum batch size can be far larger than any real batch.
        var batch = new List<AppendRequest<TContext>>();

        try
        {
            while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                // Drain what's already queued
                while (batch.Count < maxBatchSize && _channel.Reader.TryRead(out var request))
                    batch.Add(request);

                // If multiple requests arrived together, more are likely in-flight - wait up to the batching delay for
                // additional requests to accumulate before committing (Nagle-style coalescing). Short-circuits as soon
                // as the batch is full.
                if (batchingDelay > TimeSpan.Zero &&
                    batch.Count >= batchingDelayThreshold &&
                    batch.Count < maxBatchSize)
                {
                    using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    delayCts.CancelAfter(batchingDelay);

                    try
                    {
                        while (batch.Count < maxBatchSize &&
                               await _channel.Reader.WaitToReadAsync(delayCts.Token).ConfigureAwait(false))
                        {
                            while (batch.Count < maxBatchSize && _channel.Reader.TryRead(out var request))
                                batch.Add(request);
                        }
                    }
                    catch (OperationCanceledException) when (delayCts.IsCancellationRequested)
                    {
                        // Delay elapsed - not a real cancellation, continue with what we have
                    }
                }

                if (batch.Count == 0)
                    continue;

                _logger.LogTrace(
                    "Batched {BatchSize}/{MaxBatchSize} requests (~{QueueCount}) queued)",
                    batch.Count,
                    maxBatchSize,
                    _channel.Reader.Count);

                try
                {
                    var start = Stopwatch.GetTimestamp();

                    await _batchProcessor.ProcessAsync(batch, ct).ConfigureAwait(false);

                    var elapsed = Stopwatch.GetElapsedTime(start);

                    _logger.LogTrace("Batch processed in {Elapsed:F2}ms", elapsed.TotalMilliseconds);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // Let the outer catch handle graceful stop.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while processing a batch");
                    FaultAll(batch, ex);
                }

                batch.Clear();
            }
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            _logger.LogTrace("Append operation canceled");
            _channel.Writer.TryComplete();
            FaultAll(batch, ex);
            DrainWithFault(_channel, ex);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "A fatal error occurred while processing appends");
            _channel.Writer.TryComplete(ex);
            FaultAll(batch, ex);
            DrainWithFault(_channel, ex);
        }
    }

    private static void FaultAll(List<AppendRequest<TContext>> entries, Exception exception)
    {
        foreach (var request in entries)
            request.TryComplete(exception);
    }

    private static void DrainWithFault(ChannelReader<AppendRequest<TContext>> reader, Exception exception)
    {
        while (reader.TryRead(out var request))
            request.TryComplete(exception);
    }
}