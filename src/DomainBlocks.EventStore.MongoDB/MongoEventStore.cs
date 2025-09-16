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
        var collection = database.GetCollection<TEventDocument>(options.EventCollectionName);
        return new MongoEventStore<TEventDocument, TPayload>(collection, options);
    }
}

public class MongoEventStore<TEventDocument, TPayload>(
    IMongoCollection<TEventDocument> collection,
    MongoEventStoreOptions<TEventDocument, TPayload> options) : IMongoEventStore<TPayload>
{
    private readonly FieldDefinition<TEventDocument, string> _streamIdField = options.StreamIdField;
    private readonly FieldDefinition<TEventDocument, long> _streamVersionField = options.StreamVersionField;

    private readonly Expression<Func<TEventDocument, long?>> _nullableStreamVersionExpression =
        AsNullable(options.StreamVersionExpression);

    private readonly Func<TEventDocument, long> _streamVersionFunc = options.StreamVersionExpression.Compile();

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<TPayload>> events,
        ExpectedStreamState expectedState = default,
        CancellationToken cancellationToken = default)
    {
        var currentVersion = await GetCurrentStreamVersionAsync(streamId, cancellationToken);

        if (expectedState.IsStreamExists && currentVersion.IsNone)
            throw WrongExpectedStreamStateException.ExpectedStreamToExist(streamId);

        if (expectedState.IsStreamDoesNotExist && !currentVersion.IsNone)
            throw WrongExpectedStreamStateException.ExpectedStreamToNotExist(streamId, currentVersion);

        if (expectedState.IsSpecificVersion && expectedState.Version != currentVersion)
            throw WrongExpectedStreamStateException.VersionConflict(streamId, expectedState, currentVersion);

        // For non-specific version expectations (i.e. Any/StreamExists), treat the current version as expected.
        if (expectedState.IsAny || expectedState.IsStreamExists)
            expectedState = ExpectedStreamState.FromVersion(currentVersion);

        var documents = ToEventDocuments(streamId, events, currentVersion);

        try
        {
            var insertManyOptions = new InsertManyOptions { IsOrdered = true };
            await collection.InsertManyAsync(documents, insertManyOptions, cancellationToken);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Consider automatically retrying if the original expectation was Any/StreamExists.
            throw WrongExpectedStreamStateException.VersionConflict(streamId, expectedState, currentVersion);
        }
    }

    public async Task<ReadStreamResult<CommittedEvent<TPayload>>> ReadStreamAsync(
        string streamId,
        StreamReadDirection direction = StreamReadDirection.Forward,
        StreamPosition? fromPosition = null,
        CancellationToken cancellationToken = default)
    {
        fromPosition ??= direction == StreamReadDirection.Forward ? StreamPosition.Start : StreamPosition.End;

        // Edge cases: empty range
        if (fromPosition.Value.IsStart && direction == StreamReadDirection.Backward ||
            fromPosition.Value.IsEnd && direction == StreamReadDirection.Forward)
            return ReadStreamResult.RangeEmpty<CommittedEvent<TPayload>>();

        var filter = Builders<TEventDocument>.Filter.Eq(_streamIdField, streamId);

        if (fromPosition.Value.IsSpecificVersion)
        {
            var streamVersion = fromPosition.Value.Version.Value.ToInt64();

            var versionFilter = direction == StreamReadDirection.Forward
                ? Builders<TEventDocument>.Filter.Gte(_streamVersionField, streamVersion)
                : Builders<TEventDocument>.Filter.Lte(_streamVersionField, streamVersion);

            filter = Builders<TEventDocument>.Filter.And(filter, versionFilter);
        }

        var sort = direction == StreamReadDirection.Forward
            ? Builders<TEventDocument>.Sort.Ascending(_streamVersionField)
            : Builders<TEventDocument>.Sort.Descending(_streamVersionField);

        var cursor = await collection
            .Find(filter)
            .Sort(sort)
            .ToCursorAsync(cancellationToken);

        if (!await cursor.MoveNextAsync(cancellationToken) || !cursor.Current.Any())
            return ReadStreamResult.NotFound<CommittedEvent<TPayload>>();

        return ReadStreamResult.Success(Enumerate());

        async IAsyncEnumerable<CommittedEvent<TPayload>> Enumerate()
        {
            using (cursor)
            {
                do
                {
                    foreach (var doc in cursor.Current)
                        yield return FromEventDocument(doc);
                } while (await cursor.MoveNextAsync(cancellationToken));
            }
        }
    }

    private static Expression<Func<TEventDocument, long?>> AsNullable(Expression<Func<TEventDocument, long>> expr)
    {
        var body = Expression.Convert(expr.Body, typeof(long?));
        return Expression.Lambda<Func<TEventDocument, long?>>(body, expr.Parameters[0]);
    }

    private async Task<StreamVersion> GetCurrentStreamVersionAsync(string streamId, CancellationToken cancellationToken)
    {
        var latestVersion = await collection
            .Find(Builders<TEventDocument>.Filter.Eq(_streamIdField, streamId))
            .Sort(Builders<TEventDocument>.Sort.Descending(_streamVersionField))
            .Limit(1)
            .Project(_nullableStreamVersionExpression)
            .FirstOrDefaultAsync(cancellationToken);

        return StreamVersion.FromInt64(latestVersion ?? -1);
    }

    private IEnumerable<TEventDocument> ToEventDocuments(
        string streamId,
        IEnumerable<UncommittedEvent<TPayload>> events,
        StreamVersion currentStreamVersion)
    {
        var committedAt = DateTime.UtcNow;

        return events.Select((@event, index) =>
        {
            var streamVersion = currentStreamVersion.Add(index + 1);
            return ToEventDocument(streamId, streamVersion, @event, committedAt);
        });
    }

    private TEventDocument ToEventDocument(
        string streamId,
        StreamVersion version,
        UncommittedEvent<TPayload> @event,
        DateTime committedAt)
    {
        var doc = options.EventDocumentMapper.ToEventDocument(streamId, version, @event, committedAt);

        var expectedVersion = version.ToInt64();
        var actualVersion = _streamVersionFunc(doc);
        EnsureMappedStreamVersionIsValid(expectedVersion, actualVersion);

        return doc;
    }

    private CommittedEvent<TPayload> FromEventDocument(TEventDocument doc)
    {
        var @event = options.EventDocumentMapper.FromEventDocument(doc);

        var expectedVersion = _streamVersionFunc(doc);
        var actualVersion = @event.Header.StreamVersion.ToInt64();
        EnsureMappedStreamVersionIsValid(expectedVersion, actualVersion);

        return @event;
    }

    private static void EnsureMappedStreamVersionIsValid(long expectedVersion, long actualVersion)
    {
        if (expectedVersion != actualVersion)
            throw new InvalidOperationException(
                $"Event document mapper produced an invalid stream version. " +
                $"Expected={expectedVersion}, Actual={actualVersion}.");
    }
}
