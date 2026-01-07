using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using DomainBlocks.EventStore.Abstractions;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public class MongoEventStoreClient<TEvent, TEventDocument>(
    IMongoCollection<TEventDocument> collection,
    MongoEventStoreClientOptions<TEvent, TEventDocument> options) :
    IEventStoreClient<TEvent>
    where TEvent : notnull
{
    private readonly IEventDocumentCodec<TEvent, TEventDocument> _eventDocumentCodec = options.DocumentCodec;

    private readonly FieldDefinition<TEventDocument, string> _streamIdField =
        options.Collection.DocumentSchema.StreamIdField;

    private readonly FieldDefinition<TEventDocument, ulong> _streamVersionField =
        options.Collection.DocumentSchema.StreamVersionField;

    private readonly Expression<Func<TEventDocument, ulong?>> _nullableStreamVersionExpression =
        AsNullable(options.Collection.DocumentSchema.StreamVersion);

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? appendOptions = null,
        CancellationToken cancellationToken = default)
    {
        appendOptions ??= AppendToStreamOptions.Default;
        var expectedState = appendOptions.ExpectedState;
        var currentState = await GetStreamStateAsync(streamId, cancellationToken).ConfigureAwait(false);

        if (!expectedState.Matches(currentState))
            throw new StreamAppendConflictException(streamId, expectedState, currentState);

        var documents = ToEventDocuments(streamId, events, currentState.Version);

        try
        {
            var insertManyOptions = new InsertManyOptions { IsOrdered = true };
            await collection.InsertManyAsync(documents, insertManyOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (MongoBulkWriteException ex) when
            (ex.WriteErrors?.Any(e => e.Category == ServerErrorCategory.DuplicateKey) is true)
        {
            // Consider automatically retrying if the original expectation was Any/StreamExists.
            throw new StreamAppendConflictException(streamId, expectedState, innerException: ex);
        }
    }

    public async IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
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
                yield return _eventDocumentCodec.Decode(doc);
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

    private async Task<StreamState> GetStreamStateAsync(string streamId, CancellationToken cancellationToken)
    {
        var latestVersion = await collection
            .Find(Builders<TEventDocument>.Filter.Eq(_streamIdField, streamId))
            .Sort(Builders<TEventDocument>.Sort.Descending(_streamVersionField))
            .Limit(1)
            .Project(_nullableStreamVersionExpression)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return latestVersion.HasValue
            ? StreamState.StreamExists(new StreamVersion(latestVersion.Value))
            : StreamState.StreamDoesNotExist;
    }

    private async Task<bool> StreamExistsAsync(string streamId, CancellationToken cancellationToken)
    {
        var streamState = await GetStreamStateAsync(streamId, cancellationToken).ConfigureAwait(false);
        return streamState.IsStreamExists;
    }

    private IEnumerable<TEventDocument> ToEventDocuments(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        StreamVersion? currentStreamVersion)
    {
        var nextVersionValue = (currentStreamVersion?.Value + 1) ?? 0;
        var createdAtUtc = DateTime.UtcNow;
        var encoder = _eventDocumentCodec.CreateEncoder();

        foreach (var @event in events)
        {
            var streamVersion = new StreamVersion(nextVersionValue++);
            yield return encoder.Encode(@event, streamId, streamVersion, createdAtUtc);
        }
    }
}