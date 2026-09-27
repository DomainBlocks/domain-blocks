using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.EventStore.MongoDB.ChangeStreams;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.Benchmarks;

/// <summary>
/// Measures what the MongoDB store does with each live document, without I/O, for <see cref="ObserverCount"/>
/// subscriptions: every observer takes the event of a change, which is decoded once for them all, and queues it.
/// </summary>
/// <remarks>
/// The change stream subject needs a server, so it is not part of this. Documents are handed to the observers one
/// after another, as the subject hands them out.
/// </remarks>
[MemoryDiagnoser]
public class MongoLiveFanOutBenchmarks
{
    private IEventDecoder<IDomainEvent, BsonValue, BsonValue> _decoder = null!;
    private ChangeStreamDocument<BsonDocument>[] _changes = null!;
    private EventFilter _filter = null!;

    [Params(1, 4, 16)]
    public int ObserverCount { get; set; }

    [Params(10_000)]
    public int EventCount { get; set; }

    /// <summary>
    /// How many in every hundred events the observers select, by event name. At a hundred they have no filter. The
    /// events that they pass over are never decoded.
    /// </summary>
    [Params(100, 1)]
    public int SelectedPercent { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var codec = EventCodec.Create(new EventCodecOptions<IDomainEvent, BsonValue, BsonValue>
        {
            TypeMap = EventTypeMap.Create(
                EventTypeMapping.ReadWrite<TestEvent>(),
                EventTypeMapping.ReadWrite<OtherTestEvent>()),
            EventSerializer = new BsonDocumentObjectSerializer(),
            MetadataSerializer = new BsonDocumentMetadataSerializer()
        });

        var events = TestEvents.Create(SerializationFormat.Bson, EventCount, metadataEntryCount: 2, SelectedPercent);

        _decoder = codec;
        _filter = SelectedPercent == 100 ? EventFilter.All : EventFilter.EventName(nameof(TestEvent));

        _changes = codec
            .Encode(events)
            .Select((x, i) => new BsonDocument
            {
                { EventLogEntry.FieldNames.Position, (long)i },
                { EventLogEntry.FieldNames.StreamId, "test-stream" },
                { EventLogEntry.FieldNames.StreamPosition, (long)i },
                { EventLogEntry.FieldNames.EventName, x.EventName },
                { EventLogEntry.FieldNames.EventData, x.EventData },
                { EventLogEntry.FieldNames.Metadata, x.Metadata },
                { EventLogEntry.FieldNames.CreatedAtUtc, NoIOEventStore.CreatedAt.UtcDateTime }
            })
            .Select(x => new ChangeStreamDocument<BsonDocument>(
                new BsonDocument { { "operationType", "insert" }, { "fullDocument", x } },
                BsonDocumentSerializer.Instance))
            .ToArray();
    }

    [Benchmark]
    public async Task<int> FanOut_NoIO()
    {
        var liveDocument = new EventLogDocument<IDomainEvent>(_decoder, includeMetadata: true);
        var observers = new Observer[ObserverCount];

        for (var i = 0; i < observers.Length; i++)
            observers[i] = new Observer(_filter);

        foreach (var change in _changes)
        {
            foreach (var observer in observers)
            {
                liveDocument.Set(change.FullDocument);
                await observer.OnNextAsync(liveDocument, CancellationToken.None);
            }
        }

        return observers.Sum(x => x.Count);
    }

    /// <summary>
    /// Takes the event of a change and queues it, as the observer of a subscription does, then takes it off again at
    /// once, in place of a subscriber on another thread, so that a run does the same work every time.
    /// </summary>
    private sealed class Observer(EventFilter filter) : IChangeStreamObserver<EventLogDocument<IDomainEvent>>
    {
        private readonly Channel<SubscriptionMessage<IDomainEvent, string, StreamPosition, LogPosition>> _channel =
            Channel.CreateBounded<SubscriptionMessage<IDomainEvent, string, StreamPosition, LogPosition>>(
                new BoundedChannelOptions(SubscriptionOptions.Default.QueueCapacity)
                {
                    SingleWriter = true,
                    SingleReader = true
                });

        public int Count { get; private set; }

        public ValueTask OnNextAsync(EventLogDocument<IDomainEvent> change, CancellationToken cancellationToken)
        {
            Count++;

            if (filter.Matches(change) &&
                (!_channel.Writer.TryWrite(SubscriptionMessage.Event(change.DecodedEvent)) ||
                 !_channel.Reader.TryRead(out _)))
            {
                throw new InvalidOperationException("The queue did not take the event.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(Exception exception, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}