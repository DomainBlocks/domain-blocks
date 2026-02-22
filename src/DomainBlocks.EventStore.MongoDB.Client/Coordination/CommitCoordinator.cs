using DomainBlocks.EventStore.MongoDB.Client.Coordination.ChangeEvents;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class CommitCoordinator
{
    public const string LeaseResourceId = "dbx_LogLease";

    private readonly IMongoDatabase _database;
    private readonly EventStoreNamespaceSettings _namespaceSettings;
    private readonly ILeaseClient _leaseClient;
    private readonly IMongoCollection<LoggedEvent> _loggedEvents;
    private readonly ILogger<CommitCoordinator> _logger;
    private readonly CancellationTokenSource _stopCts = new();

    public CommitCoordinator(
        IMongoClient mongoClient,
        EventStoreNamespaceSettings namespaceSettings,
        ILeaseClient leaseClient,
        ILogger<CommitCoordinator> logger)
    {
        _database = mongoClient.GetDatabase(namespaceSettings.DatabaseName)
            .WithReadConcern(ReadConcern.Majority)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _namespaceSettings = namespaceSettings;
        _leaseClient = leaseClient;
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

        var leaseTask = RunLeaseContenderAsync(cancellationToken);

        var changeEventResolver = new ChangeEventResolver(_namespaceSettings);

        await subscription.ForEachAsync(
            (change, _) =>
            {
                var hasChangeEvent = changeEventResolver.TryResolve(change, out var changeEvent);
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
            var result = await _leaseClient.AcquireLeaseAsync<LogLeaseState>(
                LeaseResourceId,
                options,
                cancellationToken);

            if (!result.IsAcquired)
                continue; // Shouldn't happen with infinite timeout

            await using var handle = result.Handle;

            await handle.TryAdvanceCommitPositionAsync(2, cancellationToken);

            await handle.LeaseLostTask.WaitAsync(cancellationToken);
        }
    }
}