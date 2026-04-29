using System.Threading.Channels;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Schema;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

public class MongoEventStoreClient3<TEvent> : IEventStoreClient<TEvent> where TEvent : notnull
{
    private readonly IMongoClient _mongoClient;
    private readonly IMongoCollection<BsonDocument> _eventLog;
    private readonly IMongoCollection<BsonDocument> _sequences;
    private readonly IEventEncoder<TEvent, BsonValue, BsonValue> _encoder;
    private readonly IEventDecoder<TEvent, BsonValue, BsonValue> _decoder;
    private readonly Channel<PendingAppend> _appendChannel;
    private readonly int _appendBatchSize;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly Task _appenderTask;

    public MongoEventStoreClient3(
        IMongoClient mongoClient,
        EventCodec<TEvent, BsonValue, BsonValue> eventCodec,
        MongoEventStoreClient2Options? options = null)
    {
        options ??= new MongoEventStoreClient2Options();

        var db = mongoClient
            .GetDatabase(options.DatabaseName)
            .WithReadConcern(ReadConcern.Majority)
            .WithReadPreference(ReadPreference.Primary)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _mongoClient = mongoClient;
        _eventLog = db.GetCollection<BsonDocument>(options.EventLogCollectionName);
        _sequences = db.GetCollection<BsonDocument>(options.SequencesCollectionName);
        _encoder = eventCodec.Encoder;
        _decoder = eventCodec.Decoder;

        _appendChannel = Channel.CreateBounded<PendingAppend>(new BoundedChannelOptions(options.AppendQueueCapacity)
        {
            SingleWriter = false,
            SingleReader = true
        });

        _appendBatchSize = options.AppendBatchSize;
    }

    public async Task AppendToStreamAsync(
        string streamId,
        IEnumerable<AppendEvent<TEvent>> events,
        AppendToStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AppendToStreamOptions();

        var bsonCommitId = new BsonBinaryData(options.CommitId, GuidRepresentation.Standard);
        var writtenAtUtc = DateTime.UtcNow;

        var eventDocuments = _encoder
            .Encode(events)
            .Select((x, i) => new BsonDocument
            {
                { EventLogEntry.FieldNames.StreamId, streamId },
                { EventLogEntry.FieldNames.CommitId, bsonCommitId },
                { EventLogEntry.FieldNames.CommitIndex, i },
                { EventLogEntry.FieldNames.EventName, x.EventName },
                { EventLogEntry.FieldNames.EventData, x.EventData },
                { EventLogEntry.FieldNames.Metadata, x.Metadata ?? BsonNull.Value },
                { EventLogEntry.FieldNames.WrittenAtUtc, writtenAtUtc }
            })
            .ToArray();

        if (eventDocuments.Length == 0)
            return;

        var pendingAppend = new PendingAppend(eventDocuments);

        using var linkedTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedTimeoutCts.CancelAfter(options.Timeout);

        try
        {
            await _appendChannel.Writer.WriteAsync(pendingAppend, linkedTimeoutCts.Token);
            await pendingAppend.Ack.Task.WaitAsync(linkedTimeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller canceled.
            throw;
        }
        catch (OperationCanceledException) when (linkedTimeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Append operation did not complete within {options.Timeout}.");
        }
    }

    public IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
        string streamId,
        ReadStreamOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    private sealed class PendingAppend(BsonDocument[] events)
    {
        public BsonDocument[] Events { get; } = events;
        public TaskCompletionSource Ack { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}