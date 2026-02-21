using DomainBlocks.EventStore.MongoDB.Client.Coordination.State;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class CommitCoordinator
{
    private readonly IMongoDatabase _database;
    private readonly EventStoreNamespaceSettings _namespaceSettings;
    private readonly ILeaseProvider _leaseProvider;
    private readonly IMongoCollection<LoggedEvent> _loggedEvents;
    private readonly ILogger<CommitCoordinator> _logger;
    private readonly CancellationTokenSource _stopCts = new();

    public CommitCoordinator(
        IMongoClient mongoClient,
        EventStoreNamespaceSettings namespaceSettings,
        ILeaseProvider leaseProvider,
        ILogger<CommitCoordinator> logger)
    {
        _database = mongoClient.GetDatabase(namespaceSettings.DatabaseName)
            .WithReadConcern(ReadConcern.Majority)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _namespaceSettings = namespaceSettings;
        _leaseProvider = leaseProvider;
        _loggedEvents = _database.GetCollection<LoggedEvent>(namespaceSettings.LoggedEventsCollectionName);
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var subscriptionOptions = new ChangeStreamSubscriptionOptions
        {
            MongoOptions = new ChangeStreamOptions
            {
                FullDocument = ChangeStreamFullDocumentOption.WhenAvailable,
                FullDocumentBeforeChange = ChangeStreamFullDocumentBeforeChangeOption.WhenAvailable
            }
        };

        using var linkedCt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);

        var subscription = _database.SubscribeToChangeStream(subscriptionOptions, _logger);
        await subscription.WaitUntilLiveAsync(linkedCt.Token);

        var state = new CommitCoordinatorState(_namespaceSettings);
        await state.LoadAsync(_database, linkedCt.Token);

        var leaseTask = RunLeaseContenderAsync(cancellationToken);

        await subscription.ForEachAsync(
            (change, _) =>
            {
                if (state.TryApply(change, out var @event))
                {
                    return default;
                }

                return default;
            },
            linkedCt.Token);
    }

    private async Task RunLeaseContenderAsync(CancellationToken cancellationToken)
    {
        var options = new AcquireLeaseOptions
        {
            AcquireTimeout = Timeout.InfiniteTimeSpan
        };

        while (!cancellationToken.IsCancellationRequested)
        {
            await using var lease =
                await _leaseProvider.AcquireLeaseAsync(LogLeaseState.ResourceId, options, cancellationToken);

            if (!lease.IsAcquired)
                continue; // Shouldn't happen with infinite timeout

            await lease.Handle.TryIncrementCounterAsync(CounterNames.CommitPosition, 1, cancellationToken);
            await Task.Delay(1000, cancellationToken);
            await lease.Handle.TryIncrementCounterAsync(CounterNames.CommitPosition, 2, cancellationToken);

            await lease.Handle.LeaseLostTask.WaitAsync(cancellationToken);
        }
    }
}