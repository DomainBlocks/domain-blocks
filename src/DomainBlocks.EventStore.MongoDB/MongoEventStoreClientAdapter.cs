using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreClientAdapter
{
    public static IMongoEventStoreClientAdapter<TSerialized> Create<TEventDocument, TSerialized>(
        IMongoDatabase database,
        MongoEventStoreOptions<TEventDocument, TSerialized> options) where TSerialized : notnull
    {
        var collection = database.GetCollection<TEventDocument>(options.EventCollectionName);
        return new MongoEventStoreClientAdapter<TEventDocument, TSerialized>(collection, options);
    }
}

public class MongoEventStoreClientAdapter<TEventDocument, TSerialized>(
    IMongoCollection<TEventDocument> collection,
    MongoEventStoreOptions<TEventDocument, TSerialized> options) :
    IMongoEventStoreClientAdapter<TSerialized> where TSerialized : notnull
{
    private readonly FieldDefinition<TEventDocument, string> _streamIdField = options.StreamIdField;
    private readonly FieldDefinition<TEventDocument, long> _streamVersionField = options.StreamVersionField;

    private readonly Expression<Func<TEventDocument, long?>> _nullableStreamVersionExpression =
        AsNullable(options.StreamVersionExpression);

    private readonly Func<TEventDocument, long> _streamVersionFunc = options.StreamVersionExpression.Compile();

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<UncommittedEvent<TSerialized>> events,
        AppendToStreamOptions? appendOptions = null,
        CancellationToken cancellationToken = default)
    {
        appendOptions ??= AppendToStreamOptions.Default;
        var expectedState = appendOptions.ExpectedState;
        var currentVersion = await GetCurrentStreamVersionAsync(streamId, cancellationToken).ConfigureAwait(false);

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
            await collection.InsertManyAsync(documents, insertManyOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Consider automatically retrying if the original expectation was Any/StreamExists.
            throw WrongExpectedStreamStateException.VersionConflict(streamId, expectedState, currentVersion);
        }
    }

    public async IAsyncEnumerable<CommittedEvent<TSerialized>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? readOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        readOptions ??= ReadStreamOptions.Default;
        var position = readOptions.Position;
        var direction = readOptions.Direction;

        // Edge cases that represent an empty sequence of events.
        if (position.IsStart && direction == StreamReadDirection.Backward ||
            position.IsEnd && direction == StreamReadDirection.Forward)
        {
            if (readOptions.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw &&
                !await StreamExistsAsync(streamId, cancellationToken).ConfigureAwait(false))
            {
                throw new StreamNotFoundException(streamId);
            }

            yield break;
        }

        var filter = Builders<TEventDocument>.Filter.Eq(_streamIdField, streamId);

        if (position.IsSpecificVersion)
        {
            var version = position.Version.Value.ToInt64();

            var versionFilter = direction == StreamReadDirection.Forward
                ? Builders<TEventDocument>.Filter.Gte(_streamVersionField, version)
                : Builders<TEventDocument>.Filter.Lte(_streamVersionField, version);

            filter = Builders<TEventDocument>.Filter.And(filter, versionFilter);
        }

        var sort = direction == StreamReadDirection.Forward
            ? Builders<TEventDocument>.Sort.Ascending(_streamVersionField)
            : Builders<TEventDocument>.Sort.Descending(_streamVersionField);

        using var cursor = await collection
            .Find(filter)
            .Sort(sort)
            .Limit(readOptions.MaxCount)
            .ToCursorAsync(cancellationToken)
            .ConfigureAwait(false);

        var isEmpty = true;

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var doc in cursor.Current)
            {
                isEmpty = false;
                yield return FromEventDocument(doc);
            }
        }

        if (isEmpty && readOptions.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw)
            throw new StreamNotFoundException(streamId);
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
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return StreamVersion.FromInt64(latestVersion ?? -1);
    }

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        var currentStreamVersion = await GetCurrentStreamVersionAsync(streamId, cancellationToken)
            .ConfigureAwait(false);

        return currentStreamVersion != StreamVersion.None;
    }

    private IEnumerable<TEventDocument> ToEventDocuments(
        string streamId,
        IEnumerable<UncommittedEvent<TSerialized>> events,
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
        UncommittedEvent<TSerialized> @event,
        DateTime committedAt)
    {
        var doc = options.EventDocumentConverter.ToEventDocument(@event, streamId, version, committedAt);

        var expectedVersion = version.ToInt64();
        var actualVersion = _streamVersionFunc(doc);
        EnsureMappedStreamVersionIsValid(expectedVersion, actualVersion);

        return doc;
    }

    private CommittedEvent<TSerialized> FromEventDocument(TEventDocument doc)
    {
        var @event = options.EventDocumentConverter.FromEventDocument(doc);

        var expectedVersion = _streamVersionFunc(doc);
        var actualVersion = @event.Header.StreamVersion.ToInt64();
        EnsureMappedStreamVersionIsValid(expectedVersion, actualVersion);

        return @event;
    }

    private static void EnsureMappedStreamVersionIsValid(long expectedVersion, long actualVersion)
    {
        if (expectedVersion != actualVersion)
        {
            throw new InvalidOperationException(
                $"Event document mapper produced an invalid stream version. " +
                $"Expected={expectedVersion}, Actual={actualVersion}.");
        }
    }
}