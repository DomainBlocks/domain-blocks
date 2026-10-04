using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreAdmin
{
    // The counter field of a sequence document, as the sequenced appender names it.
    private const string SequenceNextFieldName = "next";

    public static async Task EnsureInitializedAsync(
        IMongoClient mongoClient,
        MongoEventStoreOptions options,
        CancellationToken cancellationToken = default)
    {
        var db = mongoClient.GetDatabase(options.DatabaseName);
        var eventLog = db.GetCollection<EventLogEntry>(options.EventLogCollectionName);
        var builder = Builders<EventLogEntry>.IndexKeys;

        CreateIndexModel<EventLogEntry>[] indexModels =
        [
            new(builder.Ascending(x => x.StreamId).Ascending(x => x.StreamPosition),
                new CreateIndexOptions
                {
                    Name = EventLogIndexNames.UniqueStreamVersion,
                    Unique = true
                }),

            new(builder.Ascending(x => x.CommitId), new CreateIndexOptions { Name = EventLogIndexNames.CommitId })
        ];

        await eventLog.Indexes.CreateManyAsync(indexModels, cancellationToken).ConfigureAwait(false);

        await SeedSequenceAsync(db, options, cancellationToken).ConfigureAwait(false);
    }

    // The appender claims log positions by incrementing a sequence document inside a transaction, and concurrent claims
    // are kept apart by conflicting on that document. It creates the document if there is none, but that is not safe on
    // a new database, where the collection does not exist either: each concurrent transaction creates the collection
    // for itself, sees no document, and claims the first position. So the document is created here, at zero, unless it
    // already exists. Every claim is then an update of the one document.
    private static async Task SeedSequenceAsync(
        IMongoDatabase db,
        MongoEventStoreOptions options,
        CancellationToken cancellationToken)
    {
        var sequences = db
            .GetCollection<BsonDocument>(options.SequencesCollectionName)
            .WithWriteConcern(WriteConcern.WMajority);

        try
        {
            await sequences
                .UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", MongoEventStore.SequenceId),
                    Builders<BsonDocument>.Update.SetOnInsert(SequenceNextFieldName, 0L),
                    new UpdateOptions { IsUpsert = true },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Created by a concurrent call.
        }
    }
}