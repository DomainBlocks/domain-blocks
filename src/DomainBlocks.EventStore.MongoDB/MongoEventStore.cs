using System.Linq.Expressions;
using DomainBlocks.EventStore.Abstractions;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStore
{
    public static IMongoEventStore<TPayload> Create<TEventDocument, TPayload>(
        IMongoDatabase database,
        MongoEventStoreOptions<TEventDocument, TPayload> options)
    {
        var collection = database.GetCollection<TEventDocument>(options.CollectionName);
        return new MongoEventStore<TEventDocument, TPayload>(collection, options);
    }

    public static Task EnsureIndexesAsync<TEventDocument, TPayload>(
        IMongoDatabase database,
        MongoEventStoreOptions<TEventDocument, TPayload> options,
        CancellationToken cancellationToken = default)
    {
        var collection = database.GetCollection<TEventDocument>(options.CollectionName);

        CreateIndexModel<TEventDocument>[] indexModels =
        [
            new(Builders<TEventDocument>.IndexKeys
                    .Ascending(options.StreamIdField)
                    .Ascending(options.StreamVersionField),
                new CreateIndexOptions { Unique = true }),

            new(Builders<TEventDocument>.IndexKeys.Ascending(options.CommittedAtField))
        ];

        return collection.Indexes.CreateManyAsync(indexModels, cancellationToken);
    }
}

public class MongoEventStore<TEventDocument, TPayload>(
    IMongoCollection<TEventDocument> collection,
    MongoEventStoreOptions<TEventDocument, TPayload> options) : IMongoEventStore<TPayload>
{
    private readonly FieldDefinition<TEventDocument, string> _streamIdField = options.StreamIdField;
    private readonly FieldDefinition<TEventDocument, long> _streamVersionField = options.StreamVersionField;

    private readonly Expression<Func<TEventDocument, long?>> _streamVersionProjection =
        ToNullable(options.StreamVersionSelector);

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<NewEventRecord<TPayload>> events,
        ExpectedStreamVersion? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        expectedVersion ??= ExpectedStreamVersion.Any;

        var currentStreamVersion = await GetCurrentStreamVersionAsync(streamId, cancellationToken);

        if (expectedVersion == ExpectedStreamVersion.Exists && currentStreamVersion == -1)
            throw new Exception("TODO");

        var expectedVersionValue = expectedVersion == ExpectedStreamVersion.Any
            ? currentStreamVersion
            : expectedVersion.Value.ToInt64();

        if (expectedVersionValue != currentStreamVersion)
            throw new WrongExpectedVersionException(streamId, expectedVersionValue, currentStreamVersion);

        var committedAt = DateTime.UtcNow;

        var documents = events.Select((@event, index) =>
        {
            var streamVersion = currentStreamVersion + 1 + index;
            return options.DocumentMapper.ToEventDocument(streamId, streamVersion, @event, committedAt);
        });

        try
        {
            var insertManyOptions = new InsertManyOptions { IsOrdered = true };
            await collection.InsertManyAsync(documents, insertManyOptions, cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new WrongExpectedVersionException(streamId, expectedVersionValue, currentStreamVersion, ex);
        }
    }

    public async Task<ReadStreamResult<EventRecord<TPayload>>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        StreamPosition? fromPosition = null,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<TEventDocument>.Filter.Eq(_streamIdField, streamId);

        fromPosition ??= direction == StreamReadDirection.Forward ? StreamPosition.Start : StreamPosition.End;

        // TODO (DS): Logic isn't quite right for corner cases, e.g. read forward from end or backward from start.
        if (fromPosition.Value > StreamPosition.Start && fromPosition.Value < StreamPosition.End)
        {
            var versionFilter = direction == StreamReadDirection.Forward
                ? Builders<TEventDocument>.Filter.Gte(_streamVersionField, fromPosition.Value.ToInt64())
                : Builders<TEventDocument>.Filter.Lte(_streamVersionField, fromPosition.Value.ToInt64());

            filter = Builders<TEventDocument>.Filter.And(filter, versionFilter);
        }

        var sort = direction == StreamReadDirection.Forward
            ? Builders<TEventDocument>.Sort.Ascending(_streamVersionField)
            : Builders<TEventDocument>.Sort.Descending(_streamVersionField);

        var cursor = await collection
            .Find(filter)
            .Sort(sort)
            .ToCursorAsync(cancellationToken);

        // TODO (DS): Is the additional !cursor.Current.Any() check needed?
        if (!await cursor.MoveNextAsync(cancellationToken) || !cursor.Current.Any())
            return ReadStreamResult<EventRecord<TPayload>>.NotFound();

        return ReadStreamResult<EventRecord<TPayload>>.Success(Enumerate());

        async IAsyncEnumerable<EventRecord<TPayload>> Enumerate()
        {
            using (cursor)
            {
                do
                {
                    foreach (var doc in cursor.Current)
                    {
                        yield return options.DocumentMapper.FromEventDocument(doc);
                    }
                } while (await cursor.MoveNextAsync(cancellationToken));
            }
        }
    }

    private static Expression<Func<TEventDocument, long?>> ToNullable(Expression<Func<TEventDocument, long>> expr)
    {
        var body = Expression.Convert(expr.Body, typeof(long?));
        return Expression.Lambda<Func<TEventDocument, long?>>(body, expr.Parameters[0]);
    }

    private async Task<long> GetCurrentStreamVersionAsync(string streamId, CancellationToken cancellationToken)
    {
        var latestVersion = await collection
            .Find(Builders<TEventDocument>.Filter.Eq(_streamIdField, streamId))
            .Sort(Builders<TEventDocument>.Sort.Descending(_streamVersionField))
            .Limit(1)
            .Project(_streamVersionProjection)
            .FirstOrDefaultAsync(cancellationToken);

        return latestVersion ?? -1;
    }
}