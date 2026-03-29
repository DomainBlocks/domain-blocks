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
    private const int MaxCatchUpBatchSize = 100;
    private const int MaxLiveBatchSize = 1000;

    private readonly ILeaseHandle<LeaseState> _handle;
    private long? _currentCommitPosition;
    private readonly IMongoCollection<BsonDocument> _requests;
    private readonly IMongoCollection<BsonDocument> _eventLog;
    private readonly IEventLogAppender _appender;
    private readonly IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> _changeStreamSubject;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopCts;
    private readonly Channel<BsonDocument> _channel;
    private IDisposable? _changeStreamAttachment;
    private Task? _runTask;
    private readonly TaskCompletionSource _livelinessTcs = new();

    public LeaderSession(
        ILeaseHandle<LeaseState> handle,
        long? initialCommitPosition,
        IEventLogAppender appender,
        IMongoCollection<BsonDocument> requests,
        IMongoCollection<BsonDocument> eventLog,
        IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject,
        ILoggerFactory loggerFactory)
    {
        _handle = handle;
        _currentCommitPosition = initialCommitPosition;
        _appender = appender;
        _requests = requests;
        _eventLog = eventLog;
        _changeStreamSubject = changeStreamSubject;
        _logger = loggerFactory.CreateLogger<LeaderSession>();
        _stopCts = CancellationTokenSource.CreateLinkedTokenSource(handle.LeaseLostToken);

        var channelOptions = new BoundedChannelOptions(capacity: MaxLiveBatchSize)
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
            // var success = await CatchUpAsync(ct).ConfigureAwait(false);
            // if (success)
            await RunLiveAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Session canceled");
        }
    }

    // private async Task<bool> CatchUpAsync(CancellationToken ct)
    // {
    //     _logger.LogInformation("Catch-up phase starting");
    //
    //     var allAlreadyCommitted = new HashSet<Guid>();
    //
    //     while (true)
    //     {
    //         ct.ThrowIfCancellationRequested();
    //
    //         // Exclude commit IDs we've already identified as committed.
    //         var filter = allAlreadyCommitted.Count > 0
    //             ? Builders<AppendRequest>.Filter.Eq(x => x.CompletedAtUtc, null) &
    //               Builders<AppendRequest>.Filter.Nin(x => x.CommitId, allAlreadyCommitted)
    //             : Builders<AppendRequest>.Filter.Eq(x => x.CompletedAtUtc, null);
    //
    //         var batch = await _requests
    //             .Find(filter)
    //             .Sort(Builders<AppendRequest>.Sort.Ascending(x => x.CreatedAtUtc))
    //             .Limit(MaxCatchUpBatchSize)
    //             .ToListAsync(ct)
    //             .ConfigureAwait(false);
    //
    //         if (batch.Count == 0)
    //             break;
    //
    //         // Of these, which are already committed in the event log?
    //         var batchCommitIds = batch.Select(r => r.CommitId);
    //         var alreadyCommitted = await GetCommittedIdsAsync(batchCommitIds, ct).ConfigureAwait(false);
    //
    //         // Track them so subsequent iterations skip them in the query too.
    //         allAlreadyCommitted.UnionWith(alreadyCommitted);
    //
    //         // Mark committed requests as completed (off the hot path).
    //         if (alreadyCommitted.Count > 0)
    //             _requestCompleter.Complete(alreadyCommitted);
    //
    //         // Only send uncommitted requests to the appender.
    //         var pending = batch.Where(r => !alreadyCommitted.Contains(r.CommitId)).ToList();
    //
    //         if (pending.Count == 0)
    //             continue;
    //
    //         var result = await _appender.AppendBatchAsync(pending, ct).ConfigureAwait(false);
    //
    //         if (!await TryAdvanceCommitPositionAsync(result, ct).ConfigureAwait(false))
    //             return false; // Step down - don't mark as complete
    //
    //         // The newly appended ones are now committed too.
    //         _requestCompleter.Complete([.. pending.Select(r => r.CommitId)]);
    //     }
    //
    //     _logger.LogInformation("Catch-up phase complete");
    //     return true;
    // }

    // private async Task<HashSet<Guid>> GetCommittedIdsAsync(IEnumerable<Guid> candidateIds, CancellationToken ct)
    // {
    //     if (_currentPosition is null)
    //         return [];
    //
    //     var filter =
    //         Builders<EventLogEntry>.Filter.In(x => x.CommitId, candidateIds) &
    //         Builders<EventLogEntry>.Filter.Lte(x => x.Position, _currentPosition.Value);
    //
    //     var ids = await _eventLog
    //         .Distinct(x => x.CommitId, filter, cancellationToken: ct)
    //         .ToListAsync(ct)
    //         .ConfigureAwait(false);
    //
    //     return [.. ids];
    // }

    // private async Task RunLiveAsync(CancellationToken ct)
    // {
    //     _logger.LogInformation("Switching to live mode");
    //
    //     _livelinessTcs.TrySetResult();
    //
    //     var batch = new List<BsonDocument>(MaxLiveBatchSize);
    //
    //     while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
    //     {
    //         batch.Clear();
    //
    //         while (batch.Count < MaxLiveBatchSize && _channel.Reader.TryRead(out var request))
    //             batch.Add(request);
    //
    //         if (batch.Count == 0)
    //             continue;
    //
    //         _logger.LogDebug("Processing batch of {BatchSize} request(s)", batch.Count);
    //
    //         var result = await _appender.AppendBatchAsync(batch, ct).ConfigureAwait(false);
    //
    //         if (!await TryAdvanceCommitPositionAsync(result, ct).ConfigureAwait(false))
    //             return; // Step down - don't mark as complete
    //
    //         _requestCompleter.Complete([..batch.Select(x => x[AppendRequest.FieldNames.CommitId].AsGuid)]);
    //     }
    // }

    private async Task RunLiveAsync(CancellationToken ct)
    {
        _logger.LogInformation("Switching to live mode");

        _livelinessTcs.TrySetResult();

        var batch = new List<BsonDocument>(MaxLiveBatchSize);

        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            batch.Clear();

            while (batch.Count < MaxLiveBatchSize && _channel.Reader.TryRead(out var request))
                batch.Add(request);

            if (batch.Count == 0)
                continue;

            _logger.LogDebug("Preparing {BatchSize} commit(s)", batch.Count);

            var result = await _appender.AppendBatchAsync(batch, ct).ConfigureAwait(false);

            await TryAdvanceCommitPositionAsync(result, ct).ConfigureAwait(false);
        }
    }

    private async Task<bool> TryAdvanceCommitPositionAsync(AppendBatchResult result, CancellationToken ct)
    {
        if (result.IsEmpty)
            return true;

        var success = await _handle.TryAdvanceCommitPositionAsync(result.PositionCount, ct).ConfigureAwait(false);
        if (success)
        {
            _currentCommitPosition = result.EndPosition;
            return true;
        }

        _logger.LogWarning("Failed to advance commit position by {Count}", result.PositionCount);
        return false;
    }
}