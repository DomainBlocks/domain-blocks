using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.Client.Appender.Events;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender;

public sealed class AppenderNode
{
    public const string LeaseResourceId = "dbx_LogLease";

    private readonly IMongoDatabase _database;
    private readonly EventStoreNamespaceSettings _namespaceSettings;
    private readonly ILeaseClient _leaseClient;
    private readonly ILogger<AppenderNode> _logger;
    private readonly Channel<AppenderEventEnvelope> _channel;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly AppenderProcess _appenderProcess = new();
    private Task? _eventConsumerTask;
    private Task? _changeStreamIngressTask;
    private Task? _leaseContenderTask;

    public AppenderNode(
        IMongoClient mongoClient,
        EventStoreNamespaceSettings namespaceSettings,
        ILeaseClient leaseClient,
        ILogger<AppenderNode> logger)
    {
        _database = mongoClient.GetDatabase(namespaceSettings.DatabaseName)
            .WithReadConcern(ReadConcern.Majority)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _namespaceSettings = namespaceSettings;
        _leaseClient = leaseClient;
        _logger = logger;

        // TODO: use bounded
        var channelOptions = new UnboundedChannelOptions()
        {
            SingleWriter = false,
            SingleReader = true
        };

        _channel = Channel.CreateUnbounded<AppenderEventEnvelope>(channelOptions);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken).ConfigureAwait(false);

        // Wait the first task to complete.
        await Task
            .WhenAny(_changeStreamIngressTask, _leaseContenderTask, _eventConsumerTask)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        // Stop everything if not already done so.
        if (!_stopCts.IsCancellationRequested)
            await _stopCts.CancelAsync().ConfigureAwait(false);

        // Await all tasks to surface any errors.
        await Task
            .WhenAll(_changeStreamIngressTask, _leaseContenderTask, _eventConsumerTask)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    [MemberNotNull(nameof(_changeStreamIngressTask), nameof(_leaseContenderTask), nameof(_eventConsumerTask))]
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var subscriptionOptions = new ChangeStreamSubscriptionOptions
        {
            MongoOptions = new ChangeStreamOptions
            {
                FullDocument = ChangeStreamFullDocumentOption.WhenAvailable,
                FullDocumentBeforeChange = ChangeStreamFullDocumentBeforeChangeOption.WhenAvailable
            }
        };

        var subscription = _database.SubscribeToChangeStream(subscriptionOptions, _logger);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);

        await subscription.WaitUntilLiveAsync(linkedCts.Token).ConfigureAwait(false);

        _eventConsumerTask = RunEventConsumerAsync();

        await InitializeAsync(linkedCts.Token).ConfigureAwait(false);

        _changeStreamIngressTask = RunChangeStreamIngressAsync(subscription);
        _leaseContenderTask = RunLeaseContenderAsync();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
    }

    private async Task ReplayRequestsAsync(CancellationToken cancellationToken)
    {
        var appendRequests = _database.GetCollection<BsonDocument>(_namespaceSettings.AppendRequestsCollectionName);

        using var cursor = await appendRequests
            .Find(Builders<BsonDocument>.Filter.Empty)
            .ToCursorAsync(cancellationToken);

        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var request in cursor.Current)
            {
                var commitId = request["_id"].AsGuid;
                var @event = new AppendRequestObserved(commitId, request);
                var envelope = new AppenderEventEnvelope(@event, AppenderEventSource.Replay);
                await _channel.Writer.WriteAsync(envelope, cancellationToken);
            }
        }
    }

    private async Task ReplayCommitEvents(CancellationToken cancellationToken)
    {
        var loggedEvents = _database.GetCollection<LoggedEvent>(_namespaceSettings.AppendRequestsCollectionName);

        var filter = Builders<LoggedEvent>.Filter.In(x => x.EventName, ["CommitRecorded", "CommitRejected"]);
        var sort = Builders<LoggedEvent>.Sort.Ascending(x => x.Position);

        using var cursor = await loggedEvents
            .Find(filter)
            .Sort(sort)
            .ToCursorAsync(cancellationToken);

        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var loggedEvent in cursor.Current)
            {
                // var commitId = loggedEvent.CommitId;
                // var envelope = new AppenderEventEnvelope(@event, AppenderEventSource.Replay);
                // await _channel.Writer.WriteAsync(@envelope, cancellationToken);
            }
        }
    }

    private async Task RunChangeStreamIngressAsync(
        IChangeStreamSubscription<ChangeStreamDocument<BsonDocument>> subscription)
    {
        var eventResolver = new ChangeStreamEventResolver(_namespaceSettings);

        await using (subscription.ConfigureAwait(false))
        {
            await subscription
                .ForEachAsync(
                    async (change, ct) =>
                    {
                        if (!eventResolver.TryResolve(change, out var @event))
                            return;

                        var envelope = new AppenderEventEnvelope(@event, AppenderEventSource.ChangeStream);
                        await _channel.Writer.WriteAsync(envelope, ct).ConfigureAwait(false);
                    },
                    _stopCts.Token)
                .ConfigureAwait(false);
        }
    }

    private async Task RunLeaseContenderAsync()
    {
        var options = new AcquireLeaseOptions
        {
            AcquireTimeout = Timeout.InfiniteTimeSpan
        };

        while (!_stopCts.IsCancellationRequested)
        {
            var result = await _leaseClient
                .AcquireLeaseAsync<LogLeaseState>(LeaseResourceId, options, _stopCts.Token)
                .ConfigureAwait(false);

            if (!result.IsAcquired)
                continue; // Shouldn't happen with infinite timeout

            var envelope = new AppenderEventEnvelope(
                new LeaseUpdateObserved(result.InitialSnapshot),
                AppenderEventSource.LocalNode);

            await _channel.Writer.WriteAsync(envelope).ConfigureAwait(false);

            var handle = result.Handle;

            await using (handle.ConfigureAwait(false))
            {
                await handle.LeaseLostTask.WaitAsync(_stopCts.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task RunEventConsumerAsync()
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(_stopCts.Token).ConfigureAwait(false))
        {
            _appenderProcess.Apply(envelope);
        }
    }
}