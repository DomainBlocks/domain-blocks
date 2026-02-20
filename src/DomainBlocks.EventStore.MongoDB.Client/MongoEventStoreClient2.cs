using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.EventStore.MongoDB.Client.Serialization;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public class MongoEventStoreClient2<TEvent> : IEventStoreClient<TEvent> where TEvent : notnull
{
    private readonly IMongoCollection<BsonDocument> _appendRequests;
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _eventEncoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _eventDecoder;

    public MongoEventStoreClient2(IMongoClient mongoClient, MongoEventStoreClientOptions2<TEvent> options)
    {
        var collectionOptions = options.CollectionOptions;
        var db = mongoClient.GetDatabase(collectionOptions.DatabaseName);

        _appendRequests = db
            .GetCollection<BsonDocument>(collectionOptions.AppendRequestsCollectionName)
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
        var commitId = new BsonBinaryData(options.CommitId, GuidRepresentation.Standard);
        var filter = Builders<BsonDocument>.Filter.Eq("_id", commitId);

        var requestEvents = _eventEncoder
            .Encode(events)
            .Select(x => new BsonDocument
            {
                [AppendRequestFieldNames.Event.EventName] = x.EventName,
                [AppendRequestFieldNames.Event.EventData] = x.EventData,
                [AppendRequestFieldNames.Event.Metadata] = x.Metadata ?? BsonNull.Value
            });

        var pipeline = new[]
        {
            new BsonDocument
            {
                ["$set"] = new BsonDocument
                {
                    [AppendRequestFieldNames.StreamId] = new BsonDocument
                    {
                        ["$ifNull"] = new BsonArray
                        {
                            $"${AppendRequestFieldNames.StreamId}",
                            streamId
                        }
                    },
                    [AppendRequestFieldNames.ExpectedStreamState] = new BsonDocument
                    {
                        ["$ifNull"] = new BsonArray
                        {
                            $"${AppendRequestFieldNames.ExpectedStreamState}",
                            options.ExpectedState.ToBsonDocument(new ExpectedStreamStateBsonSerializer())
                        }
                    },
                    [AppendRequestFieldNames.Events] = new BsonDocument
                    {
                        ["$ifNull"] = new BsonArray
                        {
                            $"${AppendRequestFieldNames.Events}",
                            new BsonArray(requestEvents)
                        }
                    },
                    [AppendRequestFieldNames.CreatedAtUtc] = new BsonDocument
                    {
                        ["$ifNull"] = new BsonArray
                        {
                            $"${AppendRequestFieldNames.CreatedAtUtc}",
                            "$$NOW"
                        }
                    },
                    [AppendRequestFieldNames.LastSeenAtUtc] = "$$NOW",
                }
            }
        };

        var update = new PipelineUpdateDefinition<BsonDocument>(pipeline);

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