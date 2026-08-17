using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;
using KurrentDB.Client;
using StreamPosition = DomainBlocks.EventStore.Abstractions.StreamPosition;

namespace DomainBlocks.EventStore.Benchmarks;

[MemoryDiagnoser]
public class EventStoreClientReadBenchmarks
{
    private const string StreamId = "test-stream";

    private static readonly JsonUtf8BytesObjectSerde EventSerde = new();
    private static readonly JsonUtf8BytesMetadataSerde MetadataSerde = new();

    private FakeKurrentDBEventStoreClient<IDomainEvent> _client = null!;
    private ReadStreamOptions _readStreamOptions = null!;

    [Params(false, true)]
    public bool IncludeMetadata { get; set; }

    //[Params(100, 1_000, 10_000)]
    [Params(10_000)]
    public uint EventCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var kurrentEvents = CreateKurrentEvents(EventCount);

        var codecOptions = new EventCodecOptions<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>()),
            EventSerde = EventSerde,
            MetadataSerde = MetadataSerde
        };

        var eventCodec = EventCodec.Create(codecOptions);
        _client = new FakeKurrentDBEventStoreClient<IDomainEvent>(kurrentEvents, eventCodec);
        _readStreamOptions = new ReadStreamOptions { IncludeMetadata = IncludeMetadata };
    }

    [Benchmark]
    public async Task ReadStream_NoIO()
    {
        // Force enumeration
        await foreach (var _ in _client.ReadStream(StreamId, _readStreamOptions))
        {
        }
    }

    private static ResolvedEvent[] CreateKurrentEvents(uint count)
    {
        var resolvedEvents = new ResolvedEvent[count];

        var kurrentMetadata = new Dictionary<string, string>
        {
            { "type", "TestEvent" },
            { "created", (DateTime.UtcNow - DateTime.UnixEpoch).Ticks.ToString() },
            { "content-type", "application/octet-stream" }
        };

        for (ulong i = 0; i < count; i++)
        {
            // Make event and metadata sizes equivalent to ensure a fair test when excluding metadata
            var @event = new TestEvent
            {
                Value1 = $"value1-{i}",
                Value2 = $"value2-{i}"
            };

            var metadata = new Dictionary<string, string>
            {
                { "Value1", $"value1-{i}" },
                { "Value2", $"value2-{i}" },
            };

            var serializedEvent = EventSerde.Serialize(@event);
            var serializedMetadata = MetadataSerde.Serialize(metadata);

            var eventRecord = new EventRecord(
                StreamId,
                Uuid.NewUuid(),
                KurrentDB.Client.StreamPosition.FromStreamRevision(i),
                new Position(i, i),
                kurrentMetadata,
                serializedEvent,
                serializedMetadata);

            resolvedEvents[i] = new ResolvedEvent(eventRecord, null, i);
        }

        return resolvedEvents;
    }

    private interface IDomainEvent;

    private sealed class TestEvent : IDomainEvent
    {
        public required string Value1 { get; init; }
        public required string Value2 { get; init; }
    }

    private sealed class FakeKurrentDBEventStoreClient<TEvent>(
        ResolvedEvent[] kurrentEvents,
        EventCodec<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventCodec) :
        IEventStoreClient<TEvent>
        where TEvent : notnull
    {
        public Task AppendToStreamAsync(
            string streamId,
            IEnumerable<AppendEvent<TEvent>> events,
            AppendToStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<ReadEvent<TEvent>> ReadStream(string streamId, ReadStreamOptions? options = null)
        {
            options ??= ReadStreamOptions.Default;

            foreach (var resolvedEvent in kurrentEvents)
            {
                var eventRecord = resolvedEvent.Event;
                var originalEventRecord = resolvedEvent.OriginalEvent;
                var metadataBytes = options.IncludeMetadata ? eventRecord.Metadata : default;

                var (@event, metadata) = eventCodec.Decoder.Decode(
                    eventRecord.EventType,
                    eventRecord.Data,
                    metadataBytes);

                var context = new ReadEventContext(
                    streamId,
                    new StreamPosition(originalEventRecord.EventNumber.ToUInt64()),
                    eventRecord.Created,
                    new LogPosition(originalEventRecord.Position.CommitPosition));

                yield return ReadEvent.Create(@event, metadata, context);
            }
        }
    }
}