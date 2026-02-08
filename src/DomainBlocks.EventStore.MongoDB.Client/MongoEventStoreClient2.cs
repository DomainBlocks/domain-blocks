using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Primitives.Identity;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public class MongoEventStoreClient2<TEvent> : IEventStoreClient<TEvent> where TEvent : notnull
{
    private readonly IMongoCollection<Schema.EventDocument2> _eventsCollection;
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _eventEncoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _eventDecoder;

    public MongoEventStoreClient2(IMongoClient mongoClient, MongoEventStoreClientOptions<TEvent> options)
    {
        var collectionOptions = options.CollectionOptions;
        var db = mongoClient.GetDatabase(collectionOptions.DatabaseName);

        _eventsCollection = db.GetCollection<Schema.EventDocument2>(collectionOptions.EventsCollectionName);
        _eventEncoder = options.EventCodec.Encoder;
        _eventDecoder = options.EventCodec.Decoder;
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= AppendToStreamOptions.Default;

        var eventDocuments = new List<Schema.EventDocument2>();
        var commitId = Guid.NewGuid();
        var index = 0;
        var createdAtUtc = DateTime.UtcNow;

        foreach (var (eventName, eventData, metadata) in _eventEncoder.Encode(events))
        {
            eventDocuments.Add(new Schema.EventDocument2
            {
                StreamId = streamId,
                CommitId = commitId,
                CommitIndex = index++,
                EventId = EventIdGenerator.Generate(streamId, commitId, index),
                EventName = eventName,
                EventData = eventData,
                Metadata = metadata ?? BsonNull.Value,
                CreatedAtUtc = createdAtUtc
            });
        }

        var commitProposed = new Schema.CommitProposed
        {
            EventCount = eventDocuments.Count,
            ExpectedStreamState = options.ExpectedState
        };

        var sysStreamId = GetCommitStreamId(streamId);

        eventDocuments.Add(new Schema.EventDocument2
        {
            StreamId = sysStreamId,
            CommitId = commitId,
            CommitIndex = 0,
            EventId = EventIdGenerator.Generate(sysStreamId, commitId, 0),
            EventName = "$dbx.sys.CommitProposed",
            EventData = commitProposed.ToBsonDocument(),
            Metadata = BsonNull.Value,
            CreatedAtUtc = createdAtUtc
        });

        await _eventsCollection.InsertManyAsync(
            eventDocuments,
            new InsertManyOptions { IsOrdered = false },
            cancellationToken);
    }

    public IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    private static string GetCommitStreamId(string streamId)
    {
        var escapedStreamId = Uri.EscapeDataString(streamId);
        return $"$dbx.sys/coord/commits/{escapedStreamId}";
    }
}