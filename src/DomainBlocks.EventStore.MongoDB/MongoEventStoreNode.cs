using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using DomainBlocks.EventStore.MongoDB.Coordination;
using DomainBlocks.EventStore.MongoDB.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

using IChangeStreamSubject = IChangeStreamSubject<ChangeStreamDocument<BsonDocument>>;

public sealed class MongoEventStoreNode : IMongoEventStoreNode
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BsonDocument> _requests;
    private readonly IMongoCollection<BsonDocument> _eventLog;
    private readonly IMongoCollection<LeaseDocument> _leases;
    private readonly Channel<BsonDocument> _requestChannel;
    private readonly CommitSubject _commitSubject;
    private readonly MongoEventStoreNodeOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MongoEventStoreNode> _logger;
    private readonly CancellationTokenSource _stopCts = new();
    private IChangeStreamSubject? _changeStreamSubject;
    private IChangeStreamConnection? _changeStreamConnection;
    private Task _publishRequestsTask = Task.CompletedTask;
    private Task _leaseContenderTask = Task.CompletedTask;
    private int _started;
    private int _disposed;

    public MongoEventStoreNode(
        IMongoClient mongoClient,
        MongoEventStoreNodeOptions options,
        ILoggerFactory? loggerFactory = null)
    {
        if (options.NodeRole is NodeRole.None)
            throw new ArgumentException("NodeRole must be specified.", nameof(options));

        loggerFactory ??= NullLoggerFactory.Instance;

        _database = mongoClient
            .GetDatabase(options.DatabaseName)
            .WithReadConcern(ReadConcern.Majority)
            .WithReadPreference(ReadPreference.Primary)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _requests = _database.GetCollection<BsonDocument>(options.RequestsCollectionName);
        _eventLog = _database.GetCollection<BsonDocument>(options.EventLogCollectionName);
        _leases = _database.GetCollection<LeaseDocument>(options.LeasesCollectionName);

        _requestChannel = Channel.CreateBounded<BsonDocument>(
            new BoundedChannelOptions(options.RequestQueueCapacity)
            {
                SingleWriter = false,
                SingleReader = true
            });

        _commitSubject = new CommitSubject(loggerFactory.CreateLogger<CommitSubject>());
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MongoEventStoreNode>();
    }

    public Task Completion => Volatile.Read(ref _started) == 0
        ? throw new InvalidOperationException("StartAsync must be called before accessing Completed.")
        : Task.WhenAll(_publishRequestsTask, _leaseContenderTask);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("StartAsync may only be called once.");

        // Link so that disposal during startup propagates as cancellation.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);

        _changeStreamSubject = await CreateChangeStreamSubjectAsync(linkedCts.Token);

        if (_options.NodeRole.HasFlag(NodeRole.Client))
        {
            var commitTracker = new CommitTracker(
                _options,
                _commitSubject,
                _loggerFactory.CreateLogger<CommitTracker>());

            _changeStreamSubject.Attach(commitTracker);

            _publishRequestsTask = PublishRequestsAsync(_requestChannel.Reader, _stopCts.Token);
        }

        if (_options.NodeRole.HasFlag(NodeRole.Leader))
        {
            var requestFeeder = new RequestFeeder(
                _requests,
                _changeStreamSubject,
                _options.WriteQueueCapacity,
                _options.WriteBatchSize,
                _loggerFactory.CreateLogger<RequestFeeder>());

            var leaderRunner = new LeaderRunner(
                requestFeeder,
                _eventLog,
                _options.WriteQueueCapacity,
                _options.WriteBatchSize,
                _loggerFactory);

            var leaseContender = new LeaseContender(
                new LeaseStore(_leases),
                _changeStreamSubject,
                leaderRunner,
                _options,
                _loggerFactory.CreateLogger<LeaseContender>());

            _leaseContenderTask = leaseContender.RunAsync(_stopCts.Token);
        }

        _changeStreamConnection = _changeStreamSubject.Connect();
    }

    public IEventStoreClient<TEvent> CreateClient<TEvent>(EventCodec<TEvent, BsonValue, BsonValue> eventCodec)
        where TEvent : notnull
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_options.NodeRole is NodeRole.Leader)
            throw new InvalidOperationException("Cannot create a client on a leader-only node.");

        var client = new MongoEventStoreClient<TEvent>(_requestChannel.Writer, _eventLog, _leases, eventCodec);
        _commitSubject.Attach(client);

        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        using (_stopCts)
        {
            _requestChannel.Writer.TryComplete();

            if (_changeStreamConnection is not null)
                await _changeStreamConnection.DisposeAsync().ConfigureAwait(false);

            await _stopCts.CancelAsync().ConfigureAwait(false);
            await _publishRequestsTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await _leaseContenderTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task<IChangeStreamSubject> CreateChangeStreamSubjectAsync(CancellationToken cancellationToken)
    {
        var filterBuilder = Builders<ChangeStreamDocument<BsonDocument>>.Filter;
        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>();
        var logger = _loggerFactory.CreateLogger("ChangeStream");

        // Both roles observe lease changes.
        var filter = filterBuilder.Eq("ns.coll", _options.LeasesCollectionName);

        if (_options.NodeRole.HasFlag(NodeRole.Client))
        {
            var eventCommitIdFilter = filterBuilder.Eq("ns.coll", _options.EventLogCollectionName) &
                                      // We only need the first event in a given commit to obtain the commit ID.
                                      filterBuilder.Eq($"fullDocument.{EventLogEntry.FieldNames.CommitIndex}", 0);

            filter |= eventCommitIdFilter;
        }

        if (_options.NodeRole.HasFlag(NodeRole.Leader))
        {
            var requestsFilter = filterBuilder.Eq("ns.coll", _options.RequestsCollectionName) &
                                 filterBuilder.Eq("operationType", "insert");

            filter |= requestsFilter;
        }

        return await ChangeStreamSubjectFactory.CreateAsync(
            _database.WatchAsync,
            pipeline.Match(filter),
            x => x.ResumeToken,
            logger: logger,
            cancellationToken: cancellationToken);
    }

    private async Task PublishRequestsAsync(ChannelReader<BsonDocument> reader, CancellationToken ct)
    {
        var batchSize = _options.RequestBatchSize;
        var batch = new List<BsonDocument>(batchSize);

        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                batch.Clear();

                while (batch.Count < batchSize && reader.TryRead(out var request))
                    batch.Add(request);

                _logger.LogDebug("Request batch size: {Count}", batch.Count);

                await _requests.InsertManyAsync(batch, new InsertManyOptions { IsOrdered = false }, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("Request consumer cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request consumer failed");
        }
    }
}