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

public sealed class MongoEventStoreNode : IAsyncDisposable
{
    private readonly IMongoClient _mongoClient;
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BsonDocument> _requests;
    private readonly IMongoCollection<BsonDocument> _eventLog;
    private readonly IMongoCollection<LeaseDocument> _leases;
    private readonly MongoEventStoreOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Lock _startLock = new();
    private readonly List<CommitTracker> _pendingCommitTrackers = [];
    private readonly CancellationTokenSource _stopCts = new();
    private IChangeStreamSubject? _changeStreamSubject;
    private IChangeStreamConnection? _changeStreamConnection;
    private Task? _leaseContenderTask;
    private int _started;
    private int _disposed;

    public MongoEventStoreNode(IMongoClient mongoClient, MongoEventStoreOptions options, ILoggerFactory loggerFactory)
    {
        _mongoClient = mongoClient;
        _database = _mongoClient.GetDatabase(options.DatabaseName);
        _requests = _database.GetCollection<BsonDocument>(options.AppendRequestsCollectionName);
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

        await MongoEventStoreAdmin.EnsureInitializedAsync(_mongoClient, _options, linkedCts.Token);

        var changeStreamSubject = await CreateChangeStreamSubjectAsync(linkedCts.Token);

        lock (_startLock)
        {
            foreach (var commitTracker in _pendingCommitTrackers)
                changeStreamSubject.Attach(commitTracker);

            _changeStreamSubject = changeStreamSubject;
            _pendingCommitTrackers.Clear();
        }

        var leaseStore = new LeaseStore(_leases);
        var leaseClient = new LeaseClient(leaseStore, _loggerFactory.CreateLogger<LeaseClient>());
        var leaseContender = new LeaseContender(leaseClient, _loggerFactory.CreateLogger<LeaseContender>());

        var leaseListener = new LeaderLeaseListener(
            _requests,
            _eventLog,
            changeStreamSubject,
            _options.Leader,
            _loggerFactory);

        _changeStreamConnection = changeStreamSubject.Connect();

        _leaseContenderTask = leaseContender.RunAsync(leaseListener, _stopCts.Token);
    }

    public IEventStoreClient<TEvent> CreateClient<TEvent>(EventCodec<TEvent, BsonValue, BsonValue> eventCodec)
        where TEvent : notnull
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var client = new MongoEventStoreClient<TEvent>(
            _requests,
            _options,
            eventCodec,
            _loggerFactory.CreateLogger<MongoEventStoreClient<TEvent>>());

        var commitTracker = new CommitTracker(client, _options, _loggerFactory.CreateLogger<CommitTracker>());

        lock (_startLock)
        {
            if (_changeStreamSubject is not null)
                _changeStreamSubject.Attach(commitTracker);
            else
                _pendingCommitTrackers.Add(commitTracker);
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

        var filter = (filterBuilder.Eq("ns.coll", _options.AppendRequestsCollectionName) &
                      filterBuilder.Eq("operationType", "insert")) |
                     (filterBuilder.Eq("ns.coll", _options.EventLogCollectionName) &
                      filterBuilder.Eq("fullDocument.eventName", EventNames.AppendBatchRecorded)) |
                     filterBuilder.Eq("ns.coll", _options.LeasesCollectionName);

        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>().Match(filter);

        var changeStreamSubject = await _database.CreateSubjectAsync(
            pipeline,
            logger: _loggerFactory.CreateLogger("ChangeStream"),
            cancellationToken: cancellationToken);

        return changeStreamSubject;
    }
}