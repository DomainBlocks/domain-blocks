using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Appender.Host;

using EncodedEvent = EncodedEvent<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>;

[SuppressMessage("ReSharper", "InconsistentlySynchronizedField")] // OK for logging
public sealed class EventAppender : IAsyncDisposable
{
    private const string GlobalPositionSequenceId = "global_position";

    private readonly IOptions<EventAppenderOptions> _options;
    private readonly SequenceAllocator _sequenceAllocator;
    private readonly IMongoCollection<Schema.StreamCommit> _commitsCollection;
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

        var sequencesCollection = db
            .GetCollection<BsonDocument>(options.Value.Mongo.SequencesCollectionName)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        var boundedChannelOptions = new BoundedChannelOptions(capacity: options.Value.QueueSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false
        };

        _options = options;
        _sequenceAllocator = new SequenceAllocator(sequencesCollection);

        _commitsCollection = db
            .GetCollection<Schema.StreamCommit>(options.Value.Mongo.StreamCommitsCollectionName)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _channel = Channel.CreateBounded<AppendWorkItem>(boundedChannelOptions);
        _logger = logger;
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<EncodedEvent> events,
        AppendToStreamOptions options,
        CancellationToken cancellationToken = default)
    {
        var expectedState = options.ExpectedState;

        var currentState = await GetStreamStateAsync(streamId, cancellationToken).ConfigureAwait(false);

        if (!expectedState.Matches(currentState))
            throw new StreamAppendConflictException(streamId, expectedState, currentState);

        var startVersion = currentState.IsStreamExists
            ? new StreamVersion(currentState.Version.Value.Value + 1)
            : new StreamVersion(0);

        var eventDocuments = ToEventDocuments(events).ToArray();
        if (eventDocuments.Length == 0)
            return;

        var commit = new Schema.StreamCommit
        {
            StreamId = streamId,
            StartStreamVersion = startVersion.ToInt64(),
            EndStreamVersion = startVersion.ToInt64() + eventDocuments.Length - 1,
            Events = eventDocuments
        };

        var workItem = new AppendWorkItem(commit, cancellationToken);

        await _channel.Writer.WriteAsync(workItem, cancellationToken);

        await workItem.Ack.Task.WaitAsync(cancellationToken);
    }

    private static IEnumerable<Schema.EventDocument> ToEventDocuments(IEnumerable<EncodedEvent> events)
    {
        foreach (var (eventName, eventData, metadata) in events)
        {
            yield return new Schema.EventDocument
            {
                EventName = eventName,
                EventData = ToRawBsonDocument(eventData),
                Metadata = metadata.IsEmpty ? BsonNull.Value : ToRawBsonDocument(metadata)
            };
        }
    }

    private static RawBsonDocument ToRawBsonDocument(ReadOnlyMemory<byte> memory)
    {
        if (!MemoryMarshal.TryGetArray(memory, out var segment) || segment.Array is null)
            return new RawBsonDocument(memory.ToArray());

        // Profile this
        switch (segment.Offset)
        {
            case 0 when segment.Count == segment.Array.Length:
                // Full array: simplest path (driver wraps internally)
                return new RawBsonDocument(segment.Array);
            case 0:
                // Prefix of array: avoid ByteBufferSlice by using length-limited buffer
                return new RawBsonDocument(new ByteArrayBuffer(segment.Array, segment.Count, isReadOnly: true));
            default:
                // True slice: need buffer + slice
                var buffer = new ByteArrayBuffer(segment.Array, isReadOnly: true);
                var slice = new ByteBufferSlice(buffer, segment.Offset, segment.Count);
                return new RawBsonDocument(slice);
        }
    }

    private async Task<StreamState> GetStreamStateAsync(string streamId, CancellationToken cancellationToken)
    {
        var latestVersion = await _commitsCollection
            .Find(Builders<Schema.StreamCommit>.Filter.Eq(x => x.StreamId, streamId))
            .Sort(Builders<Schema.StreamCommit>.Sort.Descending(x => x.EndStreamVersion))
            .Project(x => (long?)x.EndStreamVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return latestVersion.HasValue
            ? StreamState.StreamExists(StreamVersion.FromInt64(latestVersion.Value))
            : StreamState.StreamDoesNotExist;
    }

    private async Task ConsumeAsync()
    {
        _logger.LogInformation("Consumer loop started");

        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(_stopCts.Token))
            {
                if (item.CancellationToken.IsCancellationRequested)
                {
                    item.Ack.TrySetCanceled(item.CancellationToken);
                    continue;
                }

                try
                {
                    using var linkedCt = CancellationTokenSource.CreateLinkedTokenSource(
                        item.CancellationToken,
                        _stopCts.Token);

                    var commit = item.Commit;

                    var globalPositionAllocation = await _sequenceAllocator
                        .AllocateNextAsync(GlobalPositionSequenceId, commit.Events.Length, linkedCt.Token)
                        .ConfigureAwait(false);

                    commit.StartGlobalPosition = globalPositionAllocation.Start;
                    commit.EndGlobalPosition = globalPositionAllocation.EndExclusive - 1;
                    commit.CommittedAtUtc = DateTime.UtcNow;

                    await _commitsCollection.InsertOneAsync(commit, cancellationToken: linkedCt.Token);

                    item.Ack.SetResult();
                }
                catch (OperationCanceledException) when (item.CancellationToken.IsCancellationRequested ||
                                                         _stopCts.IsCancellationRequested)
                {
                    var token = item.CancellationToken.IsCancellationRequested
                        ? item.CancellationToken
                        : _stopCts.Token;

                    item.Ack.TrySetCanceled(token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Append failed");
                    item.Ack.TrySetException(ex);
                }
            }
        }
        catch (OperationCanceledException) when (_stopCts.IsCancellationRequested)
        {
            // Expected during shutdown
            _logger.LogDebug("Consumer loop canceled by stop token");

            _channel.Writer.TryComplete();

            while (_channel.Reader.TryRead(out var nextItem))
                nextItem.Ack.TrySetCanceled(_stopCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Consumer loop failed");

            _channel.Writer.TryComplete(ex);

            while (_channel.Reader.TryRead(out var nextItem))
                nextItem.Ack.TrySetException(ex);

            throw;
        }
        finally
        {
            _logger.LogInformation("Consumer loop stopped");
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

        _logger.LogInformation("Disposing");

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

    private sealed class AppendWorkItem(Schema.StreamCommit commit, CancellationToken cancellationToken)
    {
        public Schema.StreamCommit Commit { get; } = commit;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TaskCompletionSource Ack { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}