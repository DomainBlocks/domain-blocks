using System.Diagnostics;
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
    private readonly MongoEventStoreNodeOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Lock _startLock = new();
    private readonly CancellationTokenSource _stopCts = new();
    private List<CommitTracker>? _pendingCommitTrackers;
    private IChangeStreamSubject? _changeStreamSubject;
    private IChangeStreamConnection? _changeStreamConnection;
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
        _requests = _database.GetCollection<BsonDocument>(options.RequestsCollectionName);
        _eventLog = _database.GetCollection<BsonDocument>(options.EventLogCollectionName);
        _leases = _database.GetCollection<LeaseDocument>(options.LeasesCollectionName);
        _options = options;
        _loggerFactory = loggerFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("StartAsync may only be called once.");

        // Link so that disposal during startup propagates as cancellation.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);

        var changeStreamSubject = await CreateChangeStreamSubjectAsync(linkedCts.Token);

        lock (_startLock)
        {
            _changeStreamSubject = changeStreamSubject;

            if (_options.NodeRole.HasFlag(NodeRole.Client))
            {
                foreach (var commitTracker in _pendingCommitTrackers ?? [])
                    changeStreamSubject.Attach(commitTracker);

                _pendingCommitTrackers = null;
            }
        }

        if (_options.NodeRole.HasFlag(NodeRole.Leader))
        {
            var leaseStore = new LeaseStore(_leases);
            var leaseClient = new LeaseClient(leaseStore, _loggerFactory.CreateLogger<LeaseClient>());
            var leaseContender = new LeaseContender(leaseClient, _loggerFactory.CreateLogger<LeaseContender>());

            var leaseListener = new LeaseListener(
                _requests,
                _eventLog,
                changeStreamSubject,
                _options.Leader,
                _loggerFactory);

            _leaseContenderTask = leaseContender.RunAsync(leaseListener, _stopCts.Token);
        }

        _changeStreamConnection = changeStreamSubject.Connect();
    }

    public IEventStoreClient<TEvent> CreateClient<TEvent>(EventCodec<TEvent, BsonValue, BsonValue> eventCodec)
        where TEvent : notnull
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_options.NodeRole is NodeRole.Leader)
            throw new InvalidOperationException("Cannot create a client on a leader-only node.");

        var client = new MongoEventStoreClient<TEvent>(
            _requests,
            _options.Client,
            eventCodec,
            _loggerFactory.CreateLogger<MongoEventStoreClient<TEvent>>());

        var commitTracker = new CommitTracker(client, _options, _loggerFactory.CreateLogger<CommitTracker>());

        lock (_startLock)
        {
            if (_changeStreamSubject is not null)
                _changeStreamSubject.Attach(commitTracker);
            else
                (_pendingCommitTrackers ??= []).Add(commitTracker);
        }

        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        using (_stopCts)
        {
            if (_changeStreamConnection is not null)
                await _changeStreamConnection.DisposeAsync().ConfigureAwait(false);

            await _stopCts.CancelAsync().ConfigureAwait(false);

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
}