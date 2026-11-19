using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.MongoDB.Sequencing;

/// <inheritdoc/>
public sealed class MongoSequencedAppender<TDocument, TContext> : IMongoSequencedAppender<TDocument, TContext>
{
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
        ILogger? logger = null) :
        this(
            new MongoBatchCommitter<TDocument>(mongoClient, binding, logger ?? NullLogger.Instance),
            appendPolicy,
            options,
            logger)
    {
    }

    internal MongoSequencedAppender(
        IBatchCommitter batchCommitter,
        IMongoSequencedAppenderPolicy<TContext>? appendPolicy,
        MongoSequencedAppenderOptions? options,
        ILogger? logger)
    {
        options ??= new MongoSequencedAppenderOptions();
        _logger = logger ?? NullLogger.Instance;

        _batchProcessor = new BatchProcessor<TContext>(
            batchCommitter,
            appendPolicy ?? DefaultSequencedAppenderPolicy<TContext>.Shared,
            options,
            _logger);

        _channel = Channel.CreateBounded<AppendRequest<TContext>>(new BoundedChannelOptions(options.QueueCapacity)
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
        try
        {
            await _batchProcessor.RunAsync(_channel.Reader, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            _logger.LogTrace("Append operation canceled");
            _channel.Writer.TryComplete();
            DrainWithFault(_channel, ex);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "A fatal error occurred while processing appends");
            _channel.Writer.TryComplete(ex);
            DrainWithFault(_channel, ex);
        }
    }

    private static void DrainWithFault(ChannelReader<AppendRequest<TContext>> reader, Exception exception)
    {
        while (reader.TryRead(out var request))
            request.TryComplete(exception);
    }
}