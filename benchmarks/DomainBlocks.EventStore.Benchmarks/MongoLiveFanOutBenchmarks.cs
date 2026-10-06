using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.Benchmarks;

using SubscriptionObserver = SubscriptionAsyncEnumerable<IDomainEvent, LogPosition>.Observer;

/// <summary>
/// Measures the MongoDB live path without I/O: <see cref="EventCount"/> changes are set on the store's one live
/// document, which is handed to the observers of <see cref="SubscriberCount"/> subscriptions, each of which selects
/// every document and takes its event. The document and the observers are the real ones. The change stream subject is
/// left out, as it needs a server, so the loop over the observers stands in for it, and does with each change what the
/// store's result selector does.
/// </summary>
[MemoryDiagnoser]
public class MongoLiveFanOutBenchmarks
{
    private EventLogDocument<IDomainEvent> _liveDocument = null!;
    private ChangeStreamDocument<BsonDocument>[] _changes = null!;

    [Params(1, 4, 16)]
    public int SubscriberCount { get; set; }

    [Params(10_000)]
    public int EventCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var codec = EventCodec.Create(new EventCodecOptions<IDomainEvent, BsonValue, BsonValue>
        {
            TypeMap = new EventTypeMapBuilder().Add<TestEvent>().Build(),
            EventSerializer = new BsonDocumentObjectSerializer(),
            MetadataSerializer = new BsonDocumentMetadataSerializer()
        });

        var events = TestEvents.Create(SerializationFormat.Bson, EventCount, metadataEntryCount: 2);

        _liveDocument = new EventLogDocument<IDomainEvent>(codec);

        _changes =
        [
            .. codec
                .Encode(events)
                .Select((x, i) => new BsonDocument
                {
                    { EventLogEntry.FieldNames.Position, (long)i },
                    { EventLogEntry.FieldNames.StreamId, "test-stream" },
                    { EventLogEntry.FieldNames.StreamPosition, (long)i },
                    { EventLogEntry.FieldNames.EventName, x.EventName },
                    { EventLogEntry.FieldNames.EventData, x.EventData },
                    { EventLogEntry.FieldNames.Metadata, x.Metadata ?? BsonNull.Value },
                    { EventLogEntry.FieldNames.CreatedAtUtc, NoIOEventStore.CreatedAt.UtcDateTime }
                })
                .Select(x => new ChangeStreamDocument<BsonDocument>(
                    new BsonDocument { { "operationType", "insert" }, { "fullDocument", x } },
                    BsonDocumentSerializer.Instance))
        ];
    }

    [Benchmark]
    public long FanOut_NoIO()
    {
        var observers = new SubscriptionObserver[SubscriberCount];

        for (var i = 0; i < observers.Length; i++)
            observers[i] = new SubscriptionObserver(SubscriptionOptions.Default.QueueCapacity, EventFilter.All);

        var checksum = 0L;

        foreach (var change in _changes)
        {
            _liveDocument.Set(change.FullDocument);

            foreach (var observer in observers)
            {
                // The observer never waits.
                observer.OnNextAsync(_liveDocument, CancellationToken.None).GetAwaiter().GetResult();

                // Whatever the observer queued is taken straight off the queue again, in place of a subscriber reading
                // on another thread, so that a run does the same work every time.
                if (observer.Reader.TryRead(out var e))
                    checksum += (long)e.Context.LogPosition.Value;
            }
        }

        foreach (var observer in observers)
            observer.Dispose();

        return checksum;
    }
}