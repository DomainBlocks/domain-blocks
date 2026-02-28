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
    private LeaderLease? _leaderLease;

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
                var leaderLease = new LeaderLease(handle, _stopCts.Token);
                Volatile.Write(ref _leaderLease, leaderLease);

                try
                {
                    await handle.LeaseLostTask.WaitAsync(_stopCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    Volatile.Write(ref _leaderLease, null);
                    leaderLease.Dispose();
                }
            }
        }
    }

    private async Task RunEventConsumerAsync()
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(_stopCts.Token).ConfigureAwait(false))
        {
            _ = _appenderProcess.HandleEvent(envelope);
        }
    }

    private sealed class LeaderLease(ILeaseHandle<LogLeaseState> handle, CancellationToken stopToken) : IDisposable
    {
        public CancellationTokenSource LinkedTokenSource { get; } =
            CancellationTokenSource.CreateLinkedTokenSource(handle.LeaseLostToken, stopToken);

        public void Dispose() => LinkedTokenSource.Dispose();
    }
}

// IAppenderCoordinator (?)
// IAppenderObserver
// IAppenderWorkScheduler

// Implementation is decision-maker / state machine
public interface INodeListener
{
    // Invoke AckRequests to acknowledge all locally completed requests. Have a sensible TTL on Succeeded/Rejected
    // status so completed requests are self-purging.
    // Client may attempt to re-request when already Succeeded/Rejected. Detect and immediately complete directly.
    // Client may attempt to re-request after TTL and request purged. Let it flow through the pipeline and have
    // idempotency mark it as succeeded. Might be hard to detect this.
    void OnStarted();

    void OnLeadershipAcquired();

    void OnLeadershipLost();

    void OnLeaderCaughtUp();

    void OnRequestSubmitted();

    void OnRequestOutcomeRecorded();

    void OnCommitBatchRecorded();

    void OnCommitPositionAdvanced();
}

public interface INodeScheduler
{
    void AckRequests();

    void AckRequest();
}

public interface ILeaderScheduler
{
    // - Play through log from checkpoint position to HW mark
    // - For normal events with commit index zero, consider that as "commit succeeded"
    // - Or, alternatively, we could record a CommitAccepted event
    // - CommitRejected signals that a given commit is permanently rejected, i.e. OCC failed for expected state
    // - Mark request document with appropriate status, e.g. Pending -> Committed/Rejected
    // Scenario 1: Leader crashes before HW mark advances - requests are retried by new leader, maybe overwriting slots
    // Scenario 2: Leader crashes after HW mark but before requests are marked as completed - next leader runs this
    // catch-up phase to ensure relevant requests are marked as completed.
    // This implies commitId cannot be unique in the dbx_logged_events collection, as we need slots above HW mark to be
    // overwritable. So, completed request status is idempotency guard. Downside: inbox retention is needed forever.
    // Once done:
    // - Schedule pending requests for fulfilment from HW+1. Bug if HW+2 or more - advancement never happens (not
    //   contiguous). Bug if HW+0 or less - dangerous because greater epoch and overwrite. Must be careful here.
    void StepUp();

    // E=42, HW=10 (expected on lease acq. observation)
    // E=43, HW=20 (actual - I never got the chance to write)
    // What are the implications?
    // I start catching up on pending requests, attempting to write into pos >= 11.
    // I know I can't overwrite my own slots.
    // I can't overwrite because 42 < 43.

    // Include epoch and HW mark. Listener should have this information.
    // Or, where does the HW mark come from when fulfilling one or more requests? Via a read on lease state?
    // We might need to be append-only within an epoch. Fix partial failures in-place - retry or abort with filler
    // records. Base nextPos in memory from HW mark known at leader acquisition.
    // This allows safe HW advancement in the background. Better guarantees. We can't overwrite what we've already
    // written.
    // HW-mark background advancer halts if there is a gap (perhaps due to bug). We can detect with timeout and step
    // down, e.g. via a watchdog.
    void FulfilRequest();
}