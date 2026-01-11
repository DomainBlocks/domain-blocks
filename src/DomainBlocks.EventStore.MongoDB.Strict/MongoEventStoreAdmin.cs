using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Strict;

public static class MongoEventStoreAdmin
{
    public static async Task EnsureIndexesAsync<TEventDocument>(
        IMongoClient mongoClient,
        EventStoreCollectionOptions collectionOptions,
        EventDocumentSchema<TEventDocument> eventDocumentSchema,
        CancellationToken cancellationToken = default)
    {
        var db = mongoClient.GetDatabase(collectionOptions.DatabaseName);
        var eventsCollection = db.GetCollection<TEventDocument>(collectionOptions.EventsCollectionName);
        //var streamCommitsCollection = db.GetCollection<StreamCommit>(collectionOptions.StreamCommitsCollectionName);

        await EnsureEventsCollectionIndexesAsync(eventsCollection, eventDocumentSchema, cancellationToken);
        //await EnsureStreamCommitsCollectionIndexesAsync(streamCommitsCollection, cancellationToken);
    }

    private static Task EnsureEventsCollectionIndexesAsync<TEventDocument>(
        IMongoCollection<TEventDocument> eventsCollection,
        EventDocumentSchema<TEventDocument> eventDocumentSchema,
        CancellationToken cancellationToken = default)
    {
        var indexBuilder = Builders<TEventDocument>.IndexKeys;

        var uniqueKey = indexBuilder
            .Ascending(eventDocumentSchema.StreamIdField)
            .Ascending(eventDocumentSchema.StreamVersionField);

        var commitIdAndStreamVersion = indexBuilder
            .Ascending(eventDocumentSchema.CommitIdField)
            .Ascending(eventDocumentSchema.StreamVersionField);

        var createdAtUtc = indexBuilder.Descending(eventDocumentSchema.CreatedAtUtcField);

        CreateIndexModel<TEventDocument>[] indexModels =
        [
            new(uniqueKey, new CreateIndexOptions { Unique = true }),
            new(commitIdAndStreamVersion),
            new(createdAtUtc)
        ];

        return eventsCollection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }

    // private static Task EnsureStreamCommitsCollectionIndexesAsync(
    //     IMongoCollection<StreamCommit> streamCommitsCollection,
    //     CancellationToken cancellationToken = default)
    // {
    //     var indexBuilder = Builders<StreamCommit>.IndexKeys;
    //
    //     var uniqueKey = indexBuilder
    //         .Ascending(x => x.StreamId)
    //         .Descending(x => x.StartStreamVersion);
    //
    //     var committedAtUtc = indexBuilder.Descending(x => x.CommittedAtUtc);
    //     var commitId = indexBuilder.Ascending(x => x.CommitId);
    //     var startGlobalPosition = indexBuilder.Ascending(x => x.StartGlobalPosition);
    //
    //     CreateIndexModel<StreamCommit>[] indexModels =
    //     [
    //         new(uniqueKey, new CreateIndexOptions { Unique = true }),
    //         new(committedAtUtc),
    //         new(commitId, new CreateIndexOptions { Unique = true }),
    //         new(startGlobalPosition, new CreateIndexOptions { Unique = true })
    //     ];
    //
    //     return streamCommitsCollection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    // }
}