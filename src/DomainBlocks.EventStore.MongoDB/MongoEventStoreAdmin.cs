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

        await CreateEventLogAsync(db, options, cancellationToken).ConfigureAwait(false);

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

            // Only appends whose caller supplied a commit ID store one, so the index holds just the IDs that a retry
            // can repeat. The pre-commit lookup's $in on commit IDs implies the filter, so it can use the index.
            new(builder.Ascending(x => x.CommitId),
                new CreateIndexOptions<EventLogEntry>
                {
                    Name = EventLogIndexNames.CommitId,
                    PartialFilterExpression = Builders<EventLogEntry>.Filter.Exists(x => x.CommitId)
                })
        ];

        await eventLog.Indexes.CreateManyAsync(indexModels, cancellationToken).ConfigureAwait(false);

        await SeedSequenceAsync(db, options, cancellationToken).ConfigureAwait(false);
    }

    // Clustering the log on its position stores events in log order with no separate _id index, which saves an index
    // write per event appended and lets reads in log order scan the log itself. A collection can be clustered only
    // when it's created, so this runs before the indexes would create it unclustered. Creating it again with the same
    // options succeeds, so repeated and concurrent calls are safe.
    private static Task CreateEventLogAsync(
        IMongoDatabase db,
        MongoEventStoreOptions options,
        CancellationToken cancellationToken)
    {
        var createOptions = new CreateCollectionOptions<EventLogEntry>
        {
            ClusteredIndex = new ClusteredIndexOptions<EventLogEntry>
            {
                Key = Builders<EventLogEntry>.IndexKeys.Ascending(x => x.Position)
            }
        };

        return db.CreateCollectionAsync(options.EventLogCollectionName, createOptions, cancellationToken);
    }

    // The appender claims log positions by incrementing a sequence document inside a transaction, and creates the
    // document if there is none. A CI run once failed with a duplicate key on the first claim on a new database, which
    // fits concurrent first appends each creating the document. Creating it here, at zero, means every claim is an
    // update of an existing document.
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
            // A concurrent call has already created the document.
        }
    }
}