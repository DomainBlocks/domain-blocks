using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Coordination;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client;

public class MongoEventStoreClient2<TEvent>(
    IMongoCollection<AppendRequest> requests,
    IAppendRequestTracker requestTracker,
    MongoEventStoreClientOptions2<TEvent> options) :
    IEventStoreClient<TEvent>
    where TEvent : notnull
{
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _eventEncoder = options.EventCodec.Encoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _eventDecoder = options.EventCodec.Decoder;

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= AppendToStreamOptions.Default;

        var request = new AppendRequest
        {
            CommitId = options.CommitId,
            StreamId = streamId,
            ExpectedStreamState = options.ExpectedState,
            Events = _eventEncoder
                .Encode(events)
                .Select(x => new PendingEvent
                {
                    EventName = x.EventName,
                    EventData = x.EventData,
                    Metadata = x.Metadata ?? BsonNull.Value
                })
                .ToArray(),
            CreatedAtUtc = DateTime.UtcNow
        };

        var commitTask = requestTracker.WaitAsync(options.CommitId, cancellationToken);

        await requests.InsertOneAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);

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