using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using System.Threading.Tasks.Sources;
using DomainBlocks.EventStore.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Appender.Host;

[SuppressMessage("ReSharper", "InconsistentlySynchronizedField")] // OK for logging
public sealed class EventAppender : IAsyncDisposable
{
    private const string GlobalPositionSequenceId = "global_position";

    private readonly IOptions<EventAppenderOptions> _options;
    private readonly SequenceAllocator _sequenceAllocator;
    private readonly IMongoCollection<Schema.StreamCommit> _commits;
    private readonly ObjectPool<AppendWorkItem> _workItemPool;
    private readonly Channel<AppendWorkItem> _channel;
    private readonly ILogger<EventAppender> _logger;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly Lock _lock = new();
    private Task? _consumerLoopTask;
    private bool _isStopped;
    private int _disposed;

    public EventAppender(
        IMongoClient mongoClient,
        IOptions<EventAppenderOptions> options,
        ILogger<EventAppender> logger)
    {
        var db = mongoClient.GetDatabase(options.Value.Mongo.DatabaseName);
        var sequences = CreateSequencesCollection(db, options.Value.Mongo);

        _options = options;
        _sequenceAllocator = new SequenceAllocator(sequences);
        _commits = CreateCommitsCollection(db, options.Value.Mongo);
        _workItemPool = CreateWorkItemPool(maximumRetained: options.Value.QueueSize);
        _channel = CreateChannel(options.Value.QueueSize);
        _logger = logger;
    }

    public async Task AppendToStreamAsync(
        string streamId,
        List<Schema.EventDocument> events,
        AppendToStreamOptions options,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
            return;

        var workItem = _workItemPool.Get();
        workItem.Init(streamId, events, options, cancellationToken);

        try
        {
            await _channel.Writer.WriteAsync(workItem, cancellationToken);

            _ = await workItem.AsValueTask();
        }
        finally
        {
            _workItemPool.Return(workItem);
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isStopped)
            {
                _logger.LogWarning("Start ignored because the consumer loop is stopped");
                return;
            }

            if (_consumerLoopTask is not null)
            {
                _logger.LogDebug("Start ignored because the consumer loop is already running");
                return;
            }

            _logger.LogInformation("Starting consumer loop");
            _consumerLoopTask = Task.Run(ConsumeAsync);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? consumerLoopTask;

        lock (_lock)
        {
            if (_isStopped)
            {
                _logger.LogDebug("Stop ignored; consumer loop already stopped");
                return;
            }

            _isStopped = true;
            consumerLoopTask = _consumerLoopTask;
        }

        _logger.LogInformation("Stopping consumer loop");

        // Stop producers first
        _channel.Writer.TryComplete();

        if (consumerLoopTask is null)
        {
            _logger.LogDebug("Stop ignored; consumer loop not started");
            return;
        }

        try
        {
            // Attempt graceful drain
            await consumerLoopTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stop canceled (game over). Force-stop the consumer loop and don't wait.
            _logger.LogWarning("Consumer loop stop canceled");
            await _stopCts.CancelAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Consumer loop stop failed");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            _logger.LogDebug("Dispose ignored; already disposed");
            return;
        }

        _logger.LogDebug("Disposing");

        try
        {
            using var disposeCts = new CancellationTokenSource(_options.Value.DisposeTimeout);
            await StopAsync(disposeCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dispose failed");
        }
        finally
        {
            _stopCts.Dispose();
            _logger.LogDebug("Disposed");
        }
    }

    private static IMongoCollection<BsonDocument> CreateSequencesCollection(
        IMongoDatabase database,
        MongoOptions options)
    {
        return database
            .GetCollection<BsonDocument>(options.SequencesCollectionName)
            .WithReadConcern(ReadConcern.Majority)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));
    }

    private static IMongoCollection<Schema.StreamCommit> CreateCommitsCollection(
        IMongoDatabase database,
        MongoOptions options)
    {
        return database
            .GetCollection<Schema.StreamCommit>(options.StreamCommitsCollectionName)
            .WithReadConcern(ReadConcern.Majority)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));
    }

    private static ObjectPool<AppendWorkItem> CreateWorkItemPool(int maximumRetained)
    {
        var provider = new DefaultObjectPoolProvider { MaximumRetained = maximumRetained };
        return provider.Create(new AppendWorkItemPolicy());
    }

    private static Channel<AppendWorkItem> CreateChannel(int queueSize)
    {
        var channelOptions = new BoundedChannelOptions(queueSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false
        };

        return Channel.CreateBounded<AppendWorkItem>(channelOptions);
    }

    private async Task ConsumeAsync()
    {
        _logger.LogInformation("Consumer loop started");

        try
        {
            while (await _channel.Reader.WaitToReadAsync(_stopCts.Token))
            {
                // Potential opportunities for batching here; revisit later.
                while (_channel.Reader.TryRead(out var item))
                    await HandleWorkItemAsync(item);
            }
        }
        catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
        {
            // Expected during shutdown
            _logger.LogDebug("Consumer loop canceled by stop token");

            _channel.Writer.TryComplete();

            while (_channel.Reader.TryRead(out var nextItem))
                nextItem.SetCanceled(_stopCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Consumer loop failed");

            _channel.Writer.TryComplete(ex);

            while (_channel.Reader.TryRead(out var nextItem))
                nextItem.SetException(ex);

            throw;
        }
        finally
        {
            _logger.LogInformation("Consumer loop stopped");
        }
    }

    private async Task HandleWorkItemAsync(AppendWorkItem item)
    {
        if (item.CancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Append canceled before start for stream '{StreamId}'", item.StreamId);
            item.SetCanceled(item.CancellationToken);
            return;
        }

        var streamId = item.StreamId;
        var expectedState = item.Options.ExpectedState;
        var eventCount = item.Events.Count;

        _logger.LogDebug(
            "Starting append for stream '{StreamId}' with expected state {ExpectedState} and {EventCount} events",
            streamId,
            expectedState,
            eventCount);

        try
        {
            using var stopLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                item.CancellationToken,
                _stopCts.Token);

            var ct = stopLinkedCts.Token;

            var currentState = await GetStreamStateAsync(streamId, ct);

            if (!expectedState.Matches(currentState))
            {
                _logger.LogDebug(
                    "Append conflict for stream '{StreamId}'; expected {ExpectedState}, actual {ActualState}",
                    streamId,
                    expectedState,
                    currentState);

                item.SetException(new StreamAppendConflictException(streamId, expectedState, currentState));
                return;
            }

            var startVersion = currentState.IsStreamExists
                ? new StreamVersion(currentState.Version.Value.Value + 1)
                : new StreamVersion(0);

            var globalPositionAllocation = await _sequenceAllocator.AllocateNextAsync(
                GlobalPositionSequenceId,
                item.Events.Count,
                ct);

            var commit = new Schema.StreamCommit
            {
                StreamId = streamId,
                StartStreamVersion = startVersion.ToInt64(),
                EndStreamVersion = startVersion.ToInt64() + item.Events.Count - 1,
                StartGlobalPosition = globalPositionAllocation.Start,
                EndGlobalPosition = globalPositionAllocation.EndExclusive - 1,
                CommittedAtUtc = DateTime.UtcNow,
                Events = item.Events
            };

            await _commits.InsertOneAsync(commit, cancellationToken: ct);

            _logger.LogInformation(
                "Committed {EventCount} events to stream '{StreamId}' " +
                "(versions {StartVersion}-{EndVersion}, global positions {StartGlobalPosition}-{EndGlobalPosition})",
                eventCount,
                streamId,
                commit.StartStreamVersion,
                commit.EndStreamVersion,
                commit.StartGlobalPosition,
                commit.EndGlobalPosition);

            item.SetResult(new AppendResult());
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Consider automatically retrying if the original expectation was Any/StreamExists.
            // Update: Consider if this is even still possible.

            _logger.LogWarning(
                ex,
                "Duplicate key conflict while appending to stream '{StreamId}' with expected state {ExpectedState}",
                streamId,
                expectedState);

            item.SetException(new StreamAppendConflictException(streamId, expectedState, innerException: ex));
        }
        catch (OperationCanceledException) when (item.CancellationToken.IsCancellationRequested ||
                                                 _stopCts.IsCancellationRequested)
        {
            var token = item.CancellationToken.IsCancellationRequested
                ? item.CancellationToken
                : _stopCts.Token;

            _logger.LogDebug("Append canceled for stream '{StreamId}'", streamId);
            item.SetCanceled(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Append failed for stream '{StreamId}'", streamId);
            item.SetException(ex);
        }
    }

    private async Task<StreamState> GetStreamStateAsync(string streamId, CancellationToken cancellationToken)
    {
        var latestVersion = await _commits
            .Find(Builders<Schema.StreamCommit>.Filter.Eq(x => x.StreamId, streamId))
            .Sort(Builders<Schema.StreamCommit>.Sort.Descending(x => x.EndStreamVersion))
            .Project(x => (long?)x.EndStreamVersion)
            .FirstOrDefaultAsync(cancellationToken);

        return latestVersion.HasValue
            ? StreamState.StreamExists(StreamVersion.FromInt64(latestVersion.Value))
            : StreamState.StreamDoesNotExist;
    }

    private sealed class AppendWorkItem : IValueTaskSource<AppendResult>
    {
        private ManualResetValueTaskSourceCore<AppendResult> _vts = new()
        {
            RunContinuationsAsynchronously = true
        };

        public string StreamId { get; private set; } = null!;
        public List<Schema.EventDocument> Events { get; private set; } = null!;
        public AppendToStreamOptions Options { get; private set; } = null!;
        public CancellationToken CancellationToken { get; private set; }

        public void Init(
            string streamId,
            List<Schema.EventDocument> eventDocuments,
            AppendToStreamOptions options,
            CancellationToken cancellationToken)
        {
            StreamId = streamId;
            Events = eventDocuments;
            Options = options;
            CancellationToken = cancellationToken;

            _vts.Reset();
        }

        public ValueTask<AppendResult> AsValueTask() => new(this, _vts.Version);

        public void SetResult(AppendResult result) => _vts.SetResult(result);

        public void SetException(Exception ex) => _vts.SetException(ex);

        public void SetCanceled(CancellationToken ct) => _vts.SetException(new OperationCanceledException(ct));

        public void Clear()
        {
            StreamId = null!;
            Events = null!;
            Options = null!;
            CancellationToken = CancellationToken.None;
        }

        // IValueTaskSource methods
        AppendResult IValueTaskSource<AppendResult>.GetResult(short token) => _vts.GetResult(token);

        ValueTaskSourceStatus IValueTaskSource<AppendResult>.GetStatus(short token) => _vts.GetStatus(token);

        void IValueTaskSource<AppendResult>.OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags) => _vts.OnCompleted(continuation, state, token, flags);
    }

    private readonly record struct AppendResult;

    private sealed class AppendWorkItemPolicy : PooledObjectPolicy<AppendWorkItem>
    {
        public override AppendWorkItem Create() => new();

        public override bool Return(AppendWorkItem workItem)
        {
            workItem.Clear();
            return true;
        }
    }
}