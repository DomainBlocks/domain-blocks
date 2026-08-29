using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;
using KurrentDB.Client;
using StreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.EventStore.Benchmarks;

[MemoryDiagnoser]
public class EventStoreClientWriteBenchmarks
{
    private const string StreamId = "test-stream";

    private static readonly JsonUtf8BytesObjectSerde EventSerde = new();
    private static readonly JsonUtf8BytesMetadataSerde MetadataSerde = new();

    private FakeKurrentDBEventStore<IDomainEvent> _client = null!;
    private AppendableEvent<IDomainEvent>[] _appendEvents = null!;

    //[Params(100, 1_000, 10_000)]
    [Params(10_000)]
    public int EventCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var consumer = new Consumer();

        var codecOptions = new EventCodecOptions<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>()),
            EventSerde = EventSerde,
            MetadataSerde = MetadataSerde,
            MetadataContributors = [new MetadataContributor(EventCount)]
        };

        var eventCodec = EventCodec.Create(codecOptions);

        _client = new FakeKurrentDBEventStore<IDomainEvent>(eventCodec, consumer);

        _appendEvents = CreateAppendEvents(EventCount);
    }

    [Benchmark]
    public Task AppendToStreamAsync_NoIO()
    {
        return _client.AppendAsync(StreamId, _appendEvents);
    }

    private static AppendableEvent<IDomainEvent>[] CreateAppendEvents(int count)
    {
        var events = new AppendableEvent<IDomainEvent>[count];

        for (var i = 0; i < count; i++)
        {
            events[i] = new AppendableEvent<IDomainEvent>(
                new TestEvent
                {
                    Value1 = $"value1-{i}",
                    Value2 = $"value2-{i}"
                },
                [KeyValuePair.Create("Value1", $"value1-{i}")]);
        }

        return events;
    }

    private interface IDomainEvent;

    private sealed class TestEvent : IDomainEvent
    {
        public required string Value1 { get; init; }
        public required string Value2 { get; init; }
    }

    private sealed class FakeKurrentDBEventStore<TEvent>(
        EventCodec<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventCodec,
        Consumer consumer) :
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
            foreach (var (eventName, eventData, metadata) in eventCodec.Encoder.Encode(events))
            {
                consumer.Consume(eventName);
                consumer.Consume(eventData);
                consumer.Consume(metadata);
            }

            return Task.CompletedTask;
        }

        public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, Position>> ReadAll(
            ReadDirection direction = ReadDirection.Forward,
            ReadOrigin<Position>? origin = null,
            ReadAllOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<ReadEvent<TEvent, string, StreamPosition, Position>> ReadStream(
            string streamId,
            ReadDirection direction = ReadDirection.Forward,
            ReadOrigin<StreamPosition>? origin = null,
            ReadStreamOptions? options = null)
        {
            throw new NotImplementedException();
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

    private sealed class MetadataContributor(int eventCount) : IMetadataContributor<IDomainEvent>
    {
        private int _counter;

        public void Contribute(IDomainEvent @event, object? contract, string eventName, MetadataWriter metadata)
        {
            metadata.Set("Value2", $"value2-{_counter}");

            _counter++;
            if (_counter == eventCount)
                _counter = 0;
        }
    }
}