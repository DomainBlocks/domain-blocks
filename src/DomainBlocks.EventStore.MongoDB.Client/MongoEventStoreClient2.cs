using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public class MongoEventStoreClient2<TEvent> : IEventStoreClient<TEvent> where TEvent : notnull
{
    private readonly IMongoCollection<AppendRequest> _appendRequests;
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _eventEncoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _eventDecoder;

    public MongoEventStoreClient2(IMongoClient mongoClient, MongoEventStoreClientOptions2<TEvent> options)
    {
        var collectionOptions = options.CollectionOptions;
        var db = mongoClient.GetDatabase(collectionOptions.DatabaseName);

        _appendRequests = db
            .GetCollection<AppendRequest>(collectionOptions.AppendRequestsCollectionName)
            .WithWriteConcern(WriteConcern.W1.With(journal: false));

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

        var filter = Builders<AppendRequest>.Filter.Eq(x => x.CommitId, options.CommitId);

        var requestEvents = _eventEncoder
            .Encode(events)
            .Select(x => new AppendRequest.Event
            {
                EventName = x.EventName,
                EventData = x.EventData,
                Metadata = x.Metadata ?? BsonNull.Value
            })
            .ToArray();

        var utcNow = DateTime.UtcNow;

        var update = Builders<AppendRequest>.Update
            .SetOnInsert(x => x.CommitId, options.CommitId)
            .SetOnInsert(x => x.StreamId, streamId)
            .SetOnInsert(x => x.ExpectedStreamState, options.ExpectedState)
            .SetOnInsert(x => x.Events, requestEvents)
            .SetOnInsert(x => x.CreatedAtUtc, utcNow)
            .Set(x => x.LastSeenAtUtc, utcNow);

        await _appendRequests.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    public IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    // private static string GetCommitStreamId(string streamId)
    // {
    //     var escapedStreamId = Uri.EscapeDataString(streamId);
    //     return $"$dbx.sys/coord/commits/{escapedStreamId}";
    // }
}