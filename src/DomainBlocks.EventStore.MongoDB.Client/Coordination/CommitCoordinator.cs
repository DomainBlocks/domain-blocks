using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class CommitCoordinator
{
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<AppendRequest> _appendRequests;
    private readonly IMongoCollection<LoggedEvent> _loggedEvents;
    private readonly CollectionNamespace _appendRequestsNamespace;
    private readonly CollectionNamespace _loggedEventsNamespace;
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
                FullDocument = ChangeStreamFullDocumentOption.WhenAvailable
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
                var appendRequests = new Dictionary<Guid, AppendRequest>();

                using (var appendRequestsCursor = await _appendRequests.FindAsync(
                           Builders<AppendRequest>.Filter.Empty,
                           cancellationToken: linkedCt.Token))
                {
                    while (await appendRequestsCursor.MoveNextAsync(linkedCt.Token))
                    {
                        foreach (var appendRequest in appendRequestsCursor.Current)
                            appendRequests.Add(appendRequest.CommitId, appendRequest);
                    }
                }

                // Everything to be controlled from here
                await subscription.ForEachAsync(
                    (document, _) =>
                    {
                        if (document.CollectionNamespace.Equals(_appendRequests.CollectionNamespace))
                        {
                            var binaryCommitId = document.DocumentKey["_id"].AsBsonBinaryData;
                            var commitId = binaryCommitId.ToGuid(GuidRepresentation.Standard);

                            if (document.OperationType == ChangeStreamOperationType.Insert)
                            {
                                var request = BsonSerializer.Deserialize<AppendRequest>(document.FullDocument);
                                appendRequests.Add(commitId, request);
                            }
                            else if (document.OperationType == ChangeStreamOperationType.Update)
                            {
                                return default;
                            }
                            else if (document.OperationType == ChangeStreamOperationType.Delete)
                            {
                                return default;
                            }
                        }

                        return default;
                    },
                    linkedCt.Token);
            }
            catch (Exception ex)
            {
            }
        }
    }

    // private async Task<CommitCoordinatorState> LoadStateAsync(
    //     long? fromPositionExclusive,
    //     CancellationToken cancellationToken)
    // {
    // }
}