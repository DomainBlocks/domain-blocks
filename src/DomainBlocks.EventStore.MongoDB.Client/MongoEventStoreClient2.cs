using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Coordination;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public class MongoEventStoreClient2<TEvent>(
    IMongoCollection<BsonDocument> requests,
    IAppendRequestTracker requestTracker,
    MongoEventStoreClientOptions2<TEvent> options) :
    IEventStoreClient<TEvent>
    where TEvent : notnull
{
    private readonly IMongoCollection<BsonDocument> _requests = requests
        .WithWriteConcern(WriteConcern.W1.With(journal: false));

    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _eventEncoder = options.EventCodec.Encoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _eventDecoder = options.EventCodec.Decoder;

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= AppendToStreamOptions.Default;

        var eventsArray = new BsonArray(
            _eventEncoder
                .Encode(events)
                .Select(x => new BsonDocument
                {
                    { PendingEvent.FieldNames.EventName, x.EventName },
                    { PendingEvent.FieldNames.EventData, x.EventData },
                    { PendingEvent.FieldNames.Metadata, x.Metadata ?? BsonNull.Value }
                }));

        var request = new BsonDocument
        {
            { AppendRequest.FieldNames.CommitId, new BsonBinaryData(options.CommitId, GuidRepresentation.Standard) },
            { AppendRequest.FieldNames.StreamId, streamId },
            { AppendRequest.FieldNames.ExpectedStreamState, SerializeExpectedStreamState(options.ExpectedState) },
            { AppendRequest.FieldNames.Events, eventsArray },
            { AppendRequest.FieldNames.CreatedAtUtc, DateTime.UtcNow }
            // CompletedAtUtc omitted — absent field matches [BsonIgnoreIfNull] on the schema
        };

        var commitTask = requestTracker.WaitAsync(options.CommitId, cancellationToken);

        await _requests.InsertOneAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);

        await commitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    private static BsonDocument SerializeExpectedStreamState(ExpectedStreamState value) =>
        value.Kind switch
        {
            ExpectedStreamStateKind.Any => new BsonDocument("kind", "any"),

            ExpectedStreamStateKind.StreamExists => new BsonDocument("kind", "streamExists"),

            ExpectedStreamStateKind.StreamDoesNotExist => new BsonDocument("kind", "streamDoesNotExist"),

            ExpectedStreamStateKind.SpecificVersion => new BsonDocument
            {
                { "kind", "version" },
                { "version", checked((long)value.Version!.Value.Value) }
            },

            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Unknown {nameof(ExpectedStreamStateKind)}: {value.Kind}")
        };
}