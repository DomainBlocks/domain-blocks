using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Coordination;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public sealed class StandaloneEventStoreNode : IMongoEventStoreNode
{
    private readonly IMongoCollection<BsonDocument> _eventLog;
    private readonly IMongoCollection<LeaseDocument> _leases;
    private readonly Channel<BsonDocument> _requestChannel;
    private readonly CommitSubject _commitSubject;
    private readonly MongoEventStoreNodeOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<StandaloneEventStoreNode> _logger;
    private readonly CancellationTokenSource _stopCts = new();
    private Task? _leaseContenderTask;
    private int _started;
    private int _disposed;

    public StandaloneEventStoreNode(
        IMongoClient mongoClient,
        MongoEventStoreNodeOptions options,
        ILoggerFactory loggerFactory)
    {
        if (options.NodeRole is NodeRole.None)
            throw new ArgumentException("NodeRole must be specified.", nameof(options));

        var db = mongoClient.GetDatabase(options.DatabaseName);
        _eventLog = db.GetCollection<BsonDocument>(options.EventLogCollectionName);
        _leases = db.GetCollection<LeaseDocument>(options.LeasesCollectionName);

        _requestChannel = Channel.CreateBounded<BsonDocument>(
            new BoundedChannelOptions(options.RequestQueueCapacity)
            {
                SingleWriter = false,
                SingleReader = true
            });

        _commitSubject = new CommitSubject(loggerFactory.CreateLogger<CommitSubject>());
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<StandaloneEventStoreNode>();
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("StartAsync may only be called once.");

        // Link so that disposal during startup propagates as cancellation.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);

        var leaseStore = new LeaseStore(_leases);
        var leaseClient = new LeaseClient(leaseStore, _loggerFactory.CreateLogger<LeaseClient>());
        var leaseContender = new LeaseContender(leaseClient, _loggerFactory.CreateLogger<LeaseContender>());

        var leaseListener = new StandaloneLeaseHandler(
            _requestChannel.Reader,
            _eventLog,
            _commitSubject,
            _options.IngestBatchSize,
            _loggerFactory);

        _leaseContenderTask = leaseContender.RunAsync(leaseListener, _stopCts.Token);

        return Task.CompletedTask;
    }

    public IEventStoreClient<TEvent> CreateClient<TEvent>(EventCodec<TEvent, BsonValue, BsonValue> eventCodec)
        where TEvent : notnull
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var client = new MongoEventStoreClient<TEvent>(
            _requestChannel.Writer,
            eventCodec,
            _loggerFactory.CreateLogger<MongoEventStoreClient<TEvent>>());

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

            await _stopCts.CancelAsync().ConfigureAwait(false);

            if (_leaseContenderTask is not null)
                await _leaseContenderTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}