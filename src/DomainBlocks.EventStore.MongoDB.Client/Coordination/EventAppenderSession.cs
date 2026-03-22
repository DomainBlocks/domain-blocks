using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class EventAppenderSession : IChangeStreamObserver<ChangeStreamDocument<BsonDocument>>, IAsyncDisposable
{
    private readonly ILeaseHandle<LeaseState> _handle;
    private long? _currentPosition;
    private readonly IMongoCollection<AppendRequest> _requests;
    private readonly IMongoCollection<EventLogEntry> _eventLog;
    private readonly IEventAppender _appender;
    private readonly IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> _changeStreamSubject;
    private readonly IAppendRequestCompleter _requestCompleter;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopCts;
    private readonly Channel<AppendRequest> _channel;
    private IDisposable? _changeStreamAttachment;
    private Task? _runTask;

    public EventAppenderSession(
        ILeaseHandle<LeaseState> handle,
        long? currentPosition,
        IEventAppender appender,
        IMongoCollection<AppendRequest> requests,
        IMongoCollection<EventLogEntry> eventLog,
        IChangeStreamSubject<ChangeStreamDocument<BsonDocument>> changeStreamSubject,
        IAppendRequestCompleter requestCompleter,
        ILoggerFactory loggerFactory)
    {
        _handle = handle;
        _currentPosition = currentPosition;
        _appender = appender;
        _requests = requests;
        _eventLog = eventLog;
        _changeStreamSubject = changeStreamSubject;
        _requestCompleter = requestCompleter;
        _logger = loggerFactory.CreateLogger<EventAppenderSession>();
        _stopCts = CancellationTokenSource.CreateLinkedTokenSource(handle.LeaseLostToken);

        var channelOptions = new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        };

        _channel = Channel.CreateUnbounded<AppendRequest>(channelOptions);
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

        if (change.OperationType == ChangeStreamOperationType.Insert)
        {
            var request = BsonSerializer.Deserialize<AppendRequest>(change.FullDocument);
            await _channel.Writer.WriteAsync(request, ct);
            return;
        }

        if (change.OperationType == ChangeStreamOperationType.Update)
        {
            // Only handle client retries (lastSeenAtUtc updated).
            // Ignore completer updates (completedAtUtc set).
            var updatedFields = change.UpdateDescription?.UpdatedFields;
            if (updatedFields is null || !updatedFields.Contains(AppendRequest.FieldNames.LastSeenAtUtc))
                return;

            var commitId = change.DocumentKey["_id"].AsGuid;

            var request = await _requests
                .Find(Builders<AppendRequest>.Filter.Eq(x => x.CommitId, commitId))
                .FirstOrDefaultAsync(ct);

            if (request is null)
            {
                _logger.LogDebug(
                    "Change stream update for missing request; CommitId={CommitId}", commitId);
                return;
            }

            await _channel.Writer.WriteAsync(request, ct);
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
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                timeoutCts.Token.Register(_stopCts.Cancel);
                await _runTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private async Task RunCoreAsync()
    {
        var ct = _stopCts.Token;

        try
        {
            var success = await CatchUpAsync(ct);
            if (success)
                await RunLiveAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Session canceled");
        }
    }

    private async Task<bool> CatchUpAsync(CancellationToken ct)
    {
        _logger.LogInformation("Catch-up phase starting");

        var allAlreadyCommitted = new HashSet<Guid>();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Exclude commit IDs we've already identified as committed.
            var filter = allAlreadyCommitted.Count > 0
                ? Builders<AppendRequest>.Filter.Eq(x => x.CompletedAtUtc, null) &
                  Builders<AppendRequest>.Filter.Nin(x => x.CommitId, allAlreadyCommitted)
                : Builders<AppendRequest>.Filter.Eq(x => x.CompletedAtUtc, null);

            var batch = await _requests
                .Find(filter)
                .Sort(Builders<AppendRequest>.Sort.Ascending(x => x.CreatedAtUtc))
                .Limit(100)
                .ToListAsync(ct);

            if (batch.Count == 0)
                break;

            // Of these, which are already committed in the event log?
            var batchCommitIds = batch.Select(r => r.CommitId);
            var alreadyCommitted = await GetCommittedIdsAsync(batchCommitIds, ct);

            // Track them so subsequent iterations skip them in the query too.
            allAlreadyCommitted.UnionWith(alreadyCommitted);

            // Mark committed requests as completed (off the hot path).
            if (alreadyCommitted.Count > 0)
                _requestCompleter.Complete(alreadyCommitted);

            // Only send uncommitted requests to the appender.
            var pending = batch.Where(r => !alreadyCommitted.Contains(r.CommitId)).ToList();

            if (pending.Count == 0)
                continue;

            var result = await _appender.AppendBatchAsync(pending, ct);

            if (!await TryAdvanceCommitPositionAsync(result, ct))
                return false; // Step down - don't mark as complete

            // The newly appended ones are now committed too.
            _requestCompleter.Complete([.. pending.Select(r => r.CommitId)]);
        }

        _logger.LogInformation("Catch-up phase complete");
        return true;
    }

    private async Task<HashSet<Guid>> GetCommittedIdsAsync(IEnumerable<Guid> candidateIds, CancellationToken ct)
    {
        if (_currentPosition is null)
            return [];

        var filter =
            Builders<EventLogEntry>.Filter.In(x => x.CommitId, candidateIds) &
            Builders<EventLogEntry>.Filter.Lte(x => x.Position, _currentPosition.Value);

        var ids = await _eventLog
            .Distinct(x => x.CommitId, filter, cancellationToken: ct)
            .ToListAsync(ct);

        return [.. ids];
    }

    private async Task RunLiveAsync(CancellationToken ct)
    {
        _logger.LogInformation("Switching to live mode");

        var batch = new List<AppendRequest>();

        while (await _channel.Reader.WaitToReadAsync(ct))
        {
            batch.Clear();

            while (_channel.Reader.TryRead(out var request))
                batch.Add(request);

            if (batch.Count == 0)
                continue;

            var result = await _appender.AppendBatchAsync(batch, ct);

            if (!await TryAdvanceCommitPositionAsync(result, ct))
                return; // Step down - don't mark as complete

            _requestCompleter.Complete([..batch.Select(x => x.CommitId)]);
        }
    }

    private async Task<bool> TryAdvanceCommitPositionAsync(AppendBatchResult result, CancellationToken ct)
    {
        if (result.IsEmpty)
            return true;

        var success = await _handle.TryAdvanceCommitPositionAsync(result.PositionCount, ct);
        if (success)
        {
            _currentPosition = result.EndPosition;
            return true;
        }

        _logger.LogWarning("Failed to advance commit position by {Count}", result.PositionCount);
        return false;
    }
}