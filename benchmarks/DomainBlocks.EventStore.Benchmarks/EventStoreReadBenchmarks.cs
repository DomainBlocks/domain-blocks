using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;
using KurrentDB.Client;
using StreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.EventStore.Benchmarks;

[MemoryDiagnoser]
public class EventStoreReadBenchmarks
{
    private const string StreamId = "test-stream";

    private static readonly JsonUtf8BytesObjectSerde EventSerde = new();
    private static readonly JsonUtf8BytesMetadataSerde MetadataSerde = new();

    private FakeKurrentDBEventStore<IDomainEvent> _eventStore = null!;
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

        var decoderOptions = new EventDecoderOptions<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>()),
            EventDeserializer = EventSerde,
            MetadataDeserializer = MetadataSerde
        };

        var decoder = EventDecoder.Create(decoderOptions);

        _eventStore = new FakeKurrentDBEventStore<IDomainEvent>(kurrentEvents, decoder);
        _readStreamOptions = new ReadStreamOptions { IncludeMetadata = IncludeMetadata };
    }

    [Benchmark]
    public async Task ReadStream_NoIO()
    {
        // Force enumeration
        await foreach (var _ in _eventStore.ReadStream(StreamId, options: _readStreamOptions))
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
                StreamPosition.FromStreamRevision(i),
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

    private sealed class FakeKurrentDBEventStore<TEvent>(
        ResolvedEvent[] kurrentEvents,
        IEventDecoder<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventDecoder) :
        IEventStore<TEvent, string, StreamPosition, Position>
        where TEvent : notnull
    {
        public Task AppendAsync(
            string streamId,
            IEnumerable<AppendableEvent<TEvent>> events,
            ExpectedStreamState<StreamPosition>? expectedState = null,
            Guid? commitId = null,
            AppendOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, Position>> ReadAll(
            ReadDirection direction = ReadDirection.Forward,
            ReadOrigin<Position>? origin = null,
            ReadAllOptions? options = null)
        {
            throw new NotImplementedException();
        }

        // ReSharper disable once AsyncMethodWithoutAwait
        public async IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, Position>> ReadStream(
            string streamId,
            ReadDirection direction = ReadDirection.Forward,
            ReadOrigin<StreamPosition>? origin = null,
            ReadStreamOptions? options = null)
        {
            options ??= ReadStreamOptions.Default;

            foreach (var resolvedEvent in kurrentEvents)
            {
                var eventRecord = resolvedEvent.Event;
                var originalEventRecord = resolvedEvent.OriginalEvent;
                var metadataBytes = options.IncludeMetadata ? eventRecord.Metadata : default;

                var (@event, metadata) = eventDecoder.Decode(
                    eventRecord.EventType,
                    eventRecord.Data,
                    metadataBytes);

                var context = ReadEventContext.Create(
                    streamId,
                    metadata,
                    eventRecord.Created,
                    originalEventRecord.EventNumber,
                    originalEventRecord.Position);

                yield return ReadEvent.Create(@event, context);
            }
        }

        public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
            ReadOrigin<Position>? origin = null,
            SubscriptionOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
            string streamId,
            ReadOrigin<StreamPosition>? origin = null,
            SubscriptionOptions? options = null)
        {
            throw new NotImplementedException();
        }
    }
}