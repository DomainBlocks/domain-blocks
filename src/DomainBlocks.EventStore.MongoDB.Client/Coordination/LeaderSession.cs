using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.Client.Schema;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

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
    private readonly TaskCompletionSource _livelinessTcs = new();

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

    public Task Liveliness => _livelinessTcs.Task;

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

        if (change.OperationType == ChangeStreamOperationType.Insert)
        {
            await _channel.Writer.WriteAsync(change.FullDocument, ct).ConfigureAwait(false);
        }
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

            var result = await _appender.AppendBatchAsync(batch, ct).ConfigureAwait(false);

            if (!await TryAdvanceCommitPositionAsync(result.Count, ct).ConfigureAwait(false))
                return false;
        }

        _logger.LogInformation("Catch-up phase complete");
        return true;
    }

    private async Task RunLiveAsync(CancellationToken ct)
    {
        _logger.LogInformation("Switching to live mode");

        _livelinessTcs.TrySetResult();

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

            // Fire without awaiting — next iteration awaits before writing.
            advanceTask = TryAdvanceCommitPositionAsync(result.Count, ct);
        }
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