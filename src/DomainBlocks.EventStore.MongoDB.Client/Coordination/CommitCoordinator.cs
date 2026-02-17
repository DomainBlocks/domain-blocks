using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class CommitCoordinator
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<AppendRequest> _appendRequests;
    private readonly IMongoCollection<LoggedEvent> _loggedEvents;
    private readonly ILogger<CommitCoordinator> _logger;
    private readonly CancellationTokenSource _stopCts = new();

    public CommitCoordinator(
        IMongoClient mongoClient,
        EventStoreCollectionOptions2 collectionOptions,
        ILogger<CommitCoordinator> logger)
    {
        _database = mongoClient.GetDatabase(collectionOptions.DatabaseName);

        _appendRequests = _database
            .GetCollection<AppendRequest>(collectionOptions.AppendRequestsCollectionName)
            .WithReadConcern(ReadConcern.Majority);

        _loggedEvents = _database
            .GetCollection<LoggedEvent>(collectionOptions.LoggedEventsCollectionName)
            .WithReadConcern(ReadConcern.Majority)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var subscriptionOptions = new ChangeStreamSubscriptionOptions
        {
            MongoOptions = new ChangeStreamOptions
            {
                FullDocument = ChangeStreamFullDocumentOption.UpdateLookup
            }
        };

        using var linkedCt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);

        while (!linkedCt.IsCancellationRequested)
        {
            try
            {
                var subscription = _database.SubscribeToChangeStream(subscriptionOptions, _logger);

                await subscription.WaitUntilLiveAsync(linkedCt.Token);

                // TODO: load state from history

                // Everything to be controlled from here
                await subscription.ForEachAsync(
                    ((document, ct) =>
                    {
                        // TODO
                        return default;
                    }),
                    linkedCt.Token);
            }
            catch (Exception ex)
            {
            }
        }
    }
}