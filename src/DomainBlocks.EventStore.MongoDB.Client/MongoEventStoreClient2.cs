using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Coordination;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using DomainBlocks.EventStore.MongoDB.Client.Serialization;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public class MongoEventStoreClient2<TEvent> : IEventStoreClient<TEvent> where TEvent : notnull
{
    private readonly IMongoCollection<BsonDocument> _appendRequests;
    private readonly IAppendRequestTracker _requestTracker;
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _eventEncoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _eventDecoder;

    public MongoEventStoreClient2(
        IMongoClient mongoClient,
        IAppendRequestTracker requestTracker,
        MongoEventStoreClientOptions2<TEvent> options)
    {
        var collectionOptions = options.NamespaceSettings;
        var db = mongoClient.GetDatabase(collectionOptions.DatabaseName);

        _appendRequests = db
            .GetCollection<BsonDocument>(collectionOptions.AppendRequestsCollectionName)
            .WithWriteConcern(WriteConcern.W1.With(journal: false));

        _requestTracker = requestTracker;
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
        var bsonCommitId = new BsonBinaryData(options.CommitId, GuidRepresentation.Standard);
        var filter = Builders<BsonDocument>.Filter.Eq("_id", bsonCommitId);

        var requestEvents = _eventEncoder
            .Encode(events)
            .Select(x => new BsonDocument
            {
                [PendingEvent.FieldNames.EventName] = x.EventName,
                [PendingEvent.FieldNames.EventData] = x.EventData,
                [PendingEvent.FieldNames.Metadata] = x.Metadata ?? BsonNull.Value
            });

        var pipeline = new[]
        {
            new BsonDocument
            {
                ["$set"] = new BsonDocument
                {
                    [AppendRequest.FieldNames.StreamId] = new BsonDocument
                    {
                        ["$ifNull"] = new BsonArray
                        {
                            $"${AppendRequest.FieldNames.StreamId}",
                            streamId
                        }
                    },
                    [AppendRequest.FieldNames.ExpectedStreamState] = new BsonDocument
                    {
                        ["$ifNull"] = new BsonArray
                        {
                            $"${AppendRequest.FieldNames.ExpectedStreamState}",
                            options.ExpectedState.ToBsonDocument(new ExpectedStreamStateBsonSerializer())
                        }
                    },
                    [AppendRequest.FieldNames.Events] = new BsonDocument
                    {
                        ["$ifNull"] = new BsonArray
                        {
                            $"${AppendRequest.FieldNames.Events}",
                            new BsonArray(requestEvents)
                        }
                    },
                    [AppendRequest.FieldNames.CreatedAtUtc] = new BsonDocument
                    {
                        ["$ifNull"] = new BsonArray
                        {
                            $"${AppendRequest.FieldNames.CreatedAtUtc}",
                            "$$NOW"
                        }
                    },
                    [AppendRequest.FieldNames.LastSeenAtUtc] = "$$NOW",
                }
            }
        };

        var update = new PipelineUpdateDefinition<BsonDocument>(pipeline);

        var commitTask = _requestTracker.WaitAsync(options.CommitId, cancellationToken);

        await _appendRequests
            .UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken)
            .ConfigureAwait(false);

        await commitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
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