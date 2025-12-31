using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using DomainBlocks.EventStore.Abstractions.Exceptions;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public static class MongoEventStoreClientAdapter
{
    public static IMongoEventStoreClientAdapter<TEventData, TMetadata> Create<TEventDocument, TEventData, TMetadata>(
        IMongoDatabase database,
        MongoEventStoreOptions<TEventDocument, TEventData, TMetadata> options)
        where TEventData : notnull
        where TMetadata : notnull
    {
        var collection = database.GetCollection<TEventDocument>(options.EventCollectionName);
        return new MongoEventStoreClientAdapter<TEventDocument, TEventData, TMetadata>(collection, options);
    }
}

public class MongoEventStoreClientAdapter<TEventDocument, TEventData, TMetadata>(
    IMongoCollection<TEventDocument> collection,
    MongoEventStoreOptions<TEventDocument, TEventData, TMetadata> options) :
    IMongoEventStoreClientAdapter<TEventData, TMetadata>
    where TEventData : notnull
    where TMetadata : notnull
{
    private readonly IEventDocumentConverter<TEventDocument, TEventData, TMetadata> _eventDocumentConverter =
        options.EventDocumentConverter;

    private readonly FieldDefinition<TEventDocument, string> _streamIdField = options.StreamIdField;
    private readonly FieldDefinition<TEventDocument, ulong> _streamVersionField = options.StreamVersionField;

    private readonly Expression<Func<TEventDocument, ulong?>> _nullableStreamVersionExpression =
        AsNullable(options.StreamVersionExpression);

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEventData, TMetadata>> events,
        AppendToStreamOptions? appendOptions = null,
        CancellationToken cancellationToken = default)
    {
        appendOptions ??= AppendToStreamOptions.Default;
        var expectedState = appendOptions.ExpectedState;
        var currentVersion = await GetCurrentStreamVersionAsync(streamId, cancellationToken).ConfigureAwait(false);

        if (expectedState.IsStreamExists && !currentVersion.HasValue)
            throw WrongExpectedStreamStateException.ExpectedStreamToExist(streamId);

        if (expectedState.IsStreamDoesNotExist && currentVersion.HasValue)
            throw WrongExpectedStreamStateException.ExpectedStreamToNotExist(streamId, currentVersion.Value);

        if (expectedState.IsSpecificVersion && expectedState.Version.Value != currentVersion)
            throw WrongExpectedStreamStateException.VersionConflict(streamId, expectedState, currentVersion);

        // For non-specific version expectations (i.e. Any/StreamExists), treat the current version as expected.
        if (expectedState.IsAny || expectedState.IsStreamExists)
        {
            expectedState = currentVersion.HasValue
                ? ExpectedStreamState.SpecificVersion(currentVersion.Value)
                : ExpectedStreamState.StreamDoesNotExist;
        }

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

    public async IAsyncEnumerable<ReadEvent<TEventData>> ReadStreamAsync(
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
            var version = position.Version.Value.Value;

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
                yield return _eventDocumentConverter.FromEventDocument(doc);
            }
        }

        if (isEmpty && readOptions.StreamNotFoundBehavior == StreamNotFoundBehavior.Throw)
            throw new StreamNotFoundException(streamId);
    }

    private static Expression<Func<TEventDocument, ulong?>> AsNullable(Expression<Func<TEventDocument, ulong>> expr)
    {
        var body = Expression.Convert(expr.Body, typeof(ulong?));
        return Expression.Lambda<Func<TEventDocument, ulong?>>(body, expr.Parameters[0]);
    }

    private async Task<StreamVersion?> GetCurrentStreamVersionAsync(
        string streamId,
        CancellationToken cancellationToken)
    {
        var latestVersion = await collection
            .Find(Builders<TEventDocument>.Filter.Eq(_streamIdField, streamId))
            .Sort(Builders<TEventDocument>.Sort.Descending(_streamVersionField))
            .Limit(1)
            .Project(_nullableStreamVersionExpression)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return latestVersion.HasValue ? new StreamVersion(latestVersion.Value) : null;
    }

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        var currentStreamVersion = await GetCurrentStreamVersionAsync(streamId, cancellationToken)
            .ConfigureAwait(false);

        return currentStreamVersion.HasValue;
    }

    private IEnumerable<TEventDocument> ToEventDocuments(
        string streamId,
        IEnumerable<AppendEvent<TEventData, TMetadata>> events,
        StreamVersion? currentStreamVersion)
    {
        var nextVersionValue = (currentStreamVersion?.Value + 1) ?? 0;
        var createdAt = DateTime.UtcNow;

        foreach (var @event in events)
        {
            var streamVersion = new StreamVersion(nextVersionValue++);
            yield return _eventDocumentConverter.ToEventDocument(@event, streamId, streamVersion, createdAt);
        }
    }
}