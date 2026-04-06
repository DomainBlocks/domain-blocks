using System.Diagnostics;
using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Coordination;
using DomainBlocks.EventStore.MongoDB.Schema;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
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
    private readonly CommitTracker _commitTracker;
    private readonly MongoEventStoreNodeOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MongoEventStoreNode> _logger;
    private readonly CancellationTokenSource _stopCts = new();
    private IChangeStreamSubject? _changeStreamSubject;
    private IChangeStreamConnection? _changeStreamConnection;
    private Task? _insertRequestsTask;
    private Task? _leaseContenderTask;
    private int _started;
    private int _disposed;

    public MongoEventStoreNode(
        IMongoClient mongoClient,
        MongoEventStoreNodeOptions options,
        ILoggerFactory loggerFactory)
    {
        if (options.NodeRole is NodeRole.None)
            throw new ArgumentException("NodeRole must be specified.", nameof(options));

        _database = mongoClient.GetDatabase(options.DatabaseName);

        _requests = _database
            .GetCollection<BsonDocument>(options.RequestsCollectionName)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _eventLog = _database.GetCollection<BsonDocument>(options.EventLogCollectionName);
        _leases = _database.GetCollection<LeaseDocument>(options.LeasesCollectionName);

        _requestChannel = Channel.CreateBounded<BsonDocument>(
            new BoundedChannelOptions(options.Client.RequestQueueCapacity)
            {
                SingleWriter = false,
                SingleReader = true
            });

        _commitTracker = new CommitTracker(options, loggerFactory.CreateLogger<CommitTracker>());
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MongoEventStoreNode>();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("StartAsync may only be called once.");

        // Link so that disposal during startup propagates as cancellation.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);

        _changeStreamSubject = await CreateChangeStreamSubjectAsync(linkedCts.Token);
        _changeStreamSubject.Attach(_commitTracker);

        if (_options.NodeRole.HasFlag(NodeRole.Client))
            _insertRequestsTask = InsertRequestsAsync(_requestChannel.Reader, _stopCts.Token);

        if (_options.NodeRole.HasFlag(NodeRole.Leader))
        {
            var leaseStore = new LeaseStore(_leases);
            var leaseClient = new LeaseClient(leaseStore, _loggerFactory.CreateLogger<LeaseClient>());
            var leaseContender = new LeaseContender(leaseClient, _loggerFactory.CreateLogger<LeaseContender>());

            // Requests collection is abstracted away behind a channel.
            // - When in-memory, pass request channel directly
            // - When not in-memory, pass in an adapter over the requests collection / change stream (with catch-up)

            var leaseListener = new LeaseListener(
                _requests,
                _eventLog,
                _changeStreamSubject,
                _options.Leader,
                _loggerFactory);

            _leaseContenderTask = leaseContender.RunAsync(leaseListener, _stopCts.Token);
        }

        _changeStreamConnection = _changeStreamSubject.Connect();
    }

    public IEventStoreClient<TEvent> CreateClient<TEvent>(EventCodec<TEvent, BsonValue, BsonValue> eventCodec)
        where TEvent : notnull
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_options.NodeRole is NodeRole.Leader)
            throw new InvalidOperationException("Cannot create a client on a leader-only node.");

        var client = new MongoEventStoreClient<TEvent>(
            _requestChannel.Writer,
            eventCodec,
            _loggerFactory.CreateLogger<MongoEventStoreClient<TEvent>>());

        _commitTracker.AddListener(client);

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

            if (_insertRequestsTask is not null)
                await _insertRequestsTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (_leaseContenderTask is not null)
                await _leaseContenderTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private async Task<IChangeStreamSubject> CreateChangeStreamSubjectAsync(CancellationToken cancellationToken)
    {
        var filterBuilder = Builders<ChangeStreamDocument<BsonDocument>>.Filter;
        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>();
        var logger = _loggerFactory.CreateLogger("ChangeStream");

        // Leader needs request inserts only, so we can use a change stream directly on the requests collection.
        if (_options.NodeRole is NodeRole.Leader)
        {
            return await _requests.CreateSubjectAsync(
                pipeline.Match(filterBuilder.Eq("operationType", "insert")),
                logger: logger,
                cancellationToken: cancellationToken);
        }

        var eventFilter = filterBuilder.Eq("ns.coll", _options.EventLogCollectionName) &
                          filterBuilder.Eq("fullDocument.eventName", EventNames.AppendBatchRecorded);

        var leaseFilter = filterBuilder.Eq("ns.coll", _options.LeasesCollectionName);
        var clientFilter = eventFilter | leaseFilter;
        FilterDefinition<ChangeStreamDocument<BsonDocument>> filter;

        if (_options.NodeRole is NodeRole.ClientLeader)
        {
            var leaderFilter = filterBuilder.Eq("ns.coll", _options.RequestsCollectionName) &
                               filterBuilder.Eq("operationType", "insert");

            filter = clientFilter | leaderFilter;
        }
        else
        {
            filter = _options.NodeRole is NodeRole.Client
                ? clientFilter
                : throw new UnreachableException($"Unknown node role '{_options.NodeRole}'.");
        }

        return await _database.CreateSubjectAsync(
            pipeline.Match(filter),
            logger: logger,
            cancellationToken: cancellationToken);
    }

    private async Task InsertRequestsAsync(ChannelReader<BsonDocument> reader, CancellationToken ct)
    {
        var batchSize = _options.Client.RequestBatchSize;
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