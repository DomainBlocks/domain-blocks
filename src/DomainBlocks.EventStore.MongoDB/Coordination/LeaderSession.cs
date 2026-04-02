using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.Schema;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Coordination;

public sealed class LeaderSession : IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>, IAsyncDisposable
{
    private readonly IMongoCollection<BsonDocument> _requests;
    private readonly IEventLogAppender _appender;
    private readonly ILeaseHandle<LeaseState> _handle;
    private readonly IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> _changeStreamSubject;
    private readonly ILogger _logger;
    private readonly int _catchUpBatchSize;
    private readonly int _liveBatchSize;
    private readonly CancellationTokenSource _stopCts;
    private readonly Channel<BsonDocument> _channel;
    private IDisposable? _changeStreamAttachment;
    private Task? _runTask;

    public LeaderSession(
        IMongoCollection<BsonDocument> requests,
        IEventLogAppender appender,
        ILeaseHandle<LeaseState> handle,
        IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject,
        LeaderOptions options,
        ILoggerFactory loggerFactory)
    {
        _requests = requests;
        _appender = appender;
        _handle = handle;
        _changeStreamSubject = changeStreamSubject;
        _logger = loggerFactory.CreateLogger<LeaderSession>();
        _catchUpBatchSize = options.CatchUpBatchSize;
        _liveBatchSize = options.LiveBatchSize;
        _stopCts = CancellationTokenSource.CreateLinkedTokenSource(handle.LeaseLostToken);

        var channelOptions = new BoundedChannelOptions(options.LiveQueueCapacity)
        {
            SingleWriter = true,
            SingleReader = true
        };

        _channel = Channel.CreateBounded<BsonDocument>(channelOptions);
    }

    public void Start()
    {
        // Attach before starting work so inserts during catch-up are buffered.
        _changeStreamAttachment = _changeStreamSubject.Attach(this);
        _runTask = RunCoreAsync();
    }

    public async ValueTask OnNextAsync(ChangeStreamDocument<BsonDocument> change, CancellationToken ct)
    {
        if (!change.CollectionNamespace.Equals(_requests.CollectionNamespace))
            return;

        if (change.OperationType is ChangeStreamOperationType.Insert)
            await _channel.Writer.WriteAsync(change.FullDocument, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        // 1. Stop receiving new change stream events.
        _changeStreamAttachment?.Dispose();

        // 2. Signal no more items - RunLiveAsync drains remaining and exits.
        _channel.Writer.TryComplete();

        // 3. Wait for work to finish, with a timeout backstop.
        using (_stopCts)
        {
            if (_runTask is not null)
            {
                _stopCts.CancelAfter(TimeSpan.FromSeconds(10));
                await _runTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                await _stopCts.CancelAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task RunCoreAsync()
    {
        var ct = _stopCts.Token;

        try
        {
            if (await CatchUpAsync(ct).ConfigureAwait(false))
                await RunLiveAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Session canceled");
        }
    }

    private async Task<bool> CatchUpAsync(CancellationToken ct)
    {
        _logger.LogInformation("Catch-up phase starting");

        var sort = Builders<BsonDocument>.Sort.Ascending(AppendRequest.FieldNames.CreatedAtUtc);
        var seenCommitIds = new HashSet<BsonValue>();
        var advanceTask = Task.FromResult(true);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var filter = seenCommitIds.Count > 0
                ? Builders<BsonDocument>.Filter.Nin(AppendRequest.FieldNames.CommitId, seenCommitIds)
                : FilterDefinition<BsonDocument>.Empty;

            var batch = await _requests
                .Find(filter)
                .Sort(sort)
                .Limit(_catchUpBatchSize)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (batch.Count == 0)
                break;

            foreach (var request in batch)
                seenCommitIds.Add(request[AppendRequest.FieldNames.CommitId]);

            // Fire prefetch immediately - may overlap with remaining advance time.
            _appender.StartPrefetch(batch, ct);

            // Await the previous advance before writing.
            if (!await advanceTask.ConfigureAwait(false))
                return false;

            var result = await _appender.FlushAsync(ct).ConfigureAwait(false);

            // Advance without awaiting.
            advanceTask = TryAdvanceCommitPositionAsync(result.Count, ct);
        }

        // Await the final advance.
        return await advanceTask.ConfigureAwait(false);
    }

    private async Task RunLiveAsync(CancellationToken ct)
    {
        _logger.LogInformation("Switching to live mode");

        var batch = new List<BsonDocument>(_liveBatchSize);
        var advanceTask = Task.FromResult(true);

        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            batch.Clear();

            while (batch.Count < _liveBatchSize && _channel.Reader.TryRead(out var request))
                batch.Add(request);

            if (batch.Count == 0)
                continue;

            // Fire prefetch immediately - runs concurrently with the previous advance.
            _appender.StartPrefetch(batch, ct);

            // Await the previous advance before writing.
            if (!await advanceTask.ConfigureAwait(false))
                return;

            _logger.LogDebug("Preparing {BatchSize} commit(s)", batch.Count);

            // Awaits the prefetch (may already be done), then builds and writes.
            var result = await _appender.FlushAsync(ct).ConfigureAwait(false);

            // Advance without awaiting.
            advanceTask = TryAdvanceCommitPositionAsync(result.Count, ct);

            if (_channel.Reader.Count != 0)
                continue;

            // Empty queue - advance immediately rather than holding the position while awaiting the next batch.
            if (!await advanceTask.ConfigureAwait(false))
                return;

            advanceTask = Task.FromResult(true);
        }

        // Await the final advance after the channel drains naturally.
        await advanceTask.ConfigureAwait(false);
    }

    private async Task<bool> TryAdvanceCommitPositionAsync(long count, CancellationToken ct)
    {
        if (count == 0)
            return true;

        var success = await _handle.TryAdvanceCommitPositionAsync(count, ct).ConfigureAwait(false);

        if (!success)
            _logger.LogWarning("Failed to advance commit position by {Count}", count);

        return success;
    }
}