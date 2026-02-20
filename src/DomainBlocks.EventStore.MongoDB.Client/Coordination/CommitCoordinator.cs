using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class CommitCoordinator
{
    private readonly IMongoDatabase _database;
    private readonly EventStoreNamespaceOptions _namespaceOptions;
    private readonly IMongoCollection<LoggedEvent> _loggedEvents;
    private readonly ILogger<CommitCoordinator> _logger;
    private readonly CancellationTokenSource _stopCts = new();

    public CommitCoordinator(
        IMongoClient mongoClient,
        EventStoreNamespaceOptions namespaceOptions,
        ILogger<CommitCoordinator> logger)
    {
        _database = mongoClient.GetDatabase(namespaceOptions.DatabaseName)
            .WithReadConcern(ReadConcern.Majority)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _namespaceOptions = namespaceOptions;
        _loggedEvents = _database.GetCollection<LoggedEvent>(namespaceOptions.LoggedEventsCollectionName);
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var subscriptionOptions = new ChangeStreamSubscriptionOptions
        {
            MongoOptions = new ChangeStreamOptions
            {
                FullDocument = ChangeStreamFullDocumentOption.WhenAvailable
            }
        };

        using var linkedCt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);

        var subscription = _database.SubscribeToChangeStream(subscriptionOptions, _logger);
        await subscription.WaitUntilLiveAsync(linkedCt.Token);

        var requests = await LoadRequestsAsync(cancellationToken);

        // Everything to be controlled from here
        await subscription.ForEachAsync(
            (change, _) =>
            {
                ApplyRequestsChange(change, requests);
                return default;
            },
            linkedCt.Token);
    }

    private async Task<Dictionary<BsonValue, BsonDocument>> LoadRequestsAsync(CancellationToken cancellationToken)
    {
        var collectionName = _namespaceOptions.AppendRequestsCollectionName;
        var appendRequests = _database.GetCollection<BsonDocument>(collectionName);

        using var appendRequestsCursor = await appendRequests.FindAsync(
            Builders<BsonDocument>.Filter.Empty,
            cancellationToken: cancellationToken);

        var requests = new Dictionary<BsonValue, BsonDocument>();

        while (await appendRequestsCursor.MoveNextAsync(cancellationToken))
        {
            foreach (var appendRequest in appendRequestsCursor.Current)
                requests.Add(appendRequest["_id"], appendRequest);
        }

        return requests;
    }

    private void ApplyRequestsChange(
        ChangeStreamDocument<BsonDocument> change,
        Dictionary<BsonValue, BsonDocument> requests)
    {
        if (!change.CollectionNamespace.Equals(_namespaceOptions.AppendRequestsCollectionNamespace))
            return;

        var commitId = change.DocumentKey["_id"];

        switch (change.OperationType)
        {
            case ChangeStreamOperationType.Insert:
                requests.Add(commitId, change.FullDocument);
                break;
            case ChangeStreamOperationType.Update:
            {
                if (!requests.TryGetValue(commitId, out var request))
                    return;

                var updatedFields = change.UpdateDescription.UpdatedFields;
                if (!updatedFields.TryGetValue(AppendRequestFieldNames.LastSeenAtUtc, out var lastSeenAtUtc))
                    return;

                request[AppendRequestFieldNames.LastSeenAtUtc] = lastSeenAtUtc;
                break;
            }
            case ChangeStreamOperationType.Delete:
                requests.Remove(commitId);
                break;
        }
    }
}