using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.Client.Appender.Events;
using DomainBlocks.EventStore.MongoDB.Client.Appender.LeaderElection;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Appender;

public sealed class AppenderNode : ILocalLeaseObserver
{
    private readonly IMongoDatabase _database;
    private readonly IAppenderEventSink _eventSink;
    private readonly ILeaderLeaseContender _leaderLeaseContender;
    private readonly EventStoreNamespaceSettings _namespaceSettings;
    private readonly ILogger<AppenderNode> _logger;
    private readonly Channel<AppenderEventEnvelope> _channel;
    private readonly CancellationTokenSource _stopCts = new();
    private Task? _eventConsumerTask;
    private Task? _changeStreamIngressTask;
    private Task? _leaderLeaseContenderTask;

    public AppenderNode(
        IMongoClient mongoClient,
        IAppenderEventSink eventSink,
        ILeaderLeaseContender leaderLeaseContender,
        EventStoreNamespaceSettings namespaceSettings,
        ILogger<AppenderNode> logger)
    {
        _database = mongoClient.GetDatabase(namespaceSettings.DatabaseName)
            .WithReadConcern(ReadConcern.Majority)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _eventSink = eventSink;
        _leaderLeaseContender = leaderLeaseContender;
        _namespaceSettings = namespaceSettings;
        _logger = logger;

        // TODO: use bounded
        var channelOptions = new UnboundedChannelOptions
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
            .WhenAny(_changeStreamIngressTask, _leaderLeaseContenderTask, _eventConsumerTask)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        // Stop everything if not already done so.
        if (!_stopCts.IsCancellationRequested)
            await _stopCts.CancelAsync().ConfigureAwait(false);

        // Await all tasks to surface any errors.
        await Task
            .WhenAll(_changeStreamIngressTask, _leaderLeaseContenderTask, _eventConsumerTask)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    [MemberNotNull(nameof(_changeStreamIngressTask), nameof(_leaderLeaseContenderTask), nameof(_eventConsumerTask))]
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
        _leaderLeaseContenderTask = _leaderLeaseContender.RunAsync(this, _stopCts.Token);
    }

    async Task ILocalLeaseObserver.OnLocalLeaseAcquired(
        ILeaseHandle<LeaseState> handle,
        CancellationToken cancellationToken)
    {
        var @event = new LocalLeaseAcquired(handle);
        var envelope = new AppenderEventEnvelope(@event, AppenderEventSource.LocalNode);
        await _channel.Writer.WriteAsync(envelope, cancellationToken);
    }

    async Task ILocalLeaseObserver.OnLocalLeaseLost(
        LeaseClaim leaseClaim,
        LeaseLostInfo leaseLostInfo,
        CancellationToken cancellationToken)
    {
        var @event = new LocalLeaseLost(leaseClaim, leaseLostInfo);
        var envelope = new AppenderEventEnvelope(@event, AppenderEventSource.LocalNode);
        await _channel.Writer.WriteAsync(envelope, cancellationToken);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await Task
            .WhenAll(
                ReplayRequestsAsync(cancellationToken),
                ReplayCommitEvents(cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task ReplayRequestsAsync(CancellationToken cancellationToken)
    {
        var appendRequests = _database.GetCollection<AppendRequest>(_namespaceSettings.AppendRequestsCollectionName);

        var filter = Builders<AppendRequest>.Filter.Empty;
        var sort = Builders<AppendRequest>.Sort.Ascending(x => x.CreatedAtUtc);
        var projection = Builders<AppendRequest>.Projection.As<BsonDocument>();

        using var cursor = await appendRequests
            .Find(filter)
            .Sort(sort)
            .Project(projection)
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        // Claim request by with epoch
        // Write events to log
        // Mark request as done, epoch fenced with CAS
        // Higher epochs can reclaim if leadership lost

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
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

        // First event is successful ack. Rejections not logged as events.
        var filter = Builders<LoggedEvent>.Filter.Eq(x => x.CommitIndex, 0);

        var sort = Builders<LoggedEvent>.Sort.Ascending(x => x.Position);

        using var cursor = await loggedEvents
            .Find(filter)
            .Sort(sort)
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var loggedEvent in cursor.Current)
            {
                var commitId = loggedEvent.CommitId;
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

    private async Task RunEventConsumerAsync()
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(_stopCts.Token).ConfigureAwait(false))
        {
            await _eventSink.OnEventAsync(envelope, _stopCts.Token).ConfigureAwait(false);
        }
    }
}