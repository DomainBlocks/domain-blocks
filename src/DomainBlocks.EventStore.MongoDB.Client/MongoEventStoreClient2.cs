using DomainBlocks.EventStore.Abstractions;
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

    public Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= AppendToStreamOptions.Default;

        throw new NotImplementedException();
    }

    public IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}