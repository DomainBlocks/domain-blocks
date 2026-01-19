using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;

namespace DomainBlocks.EventStore.Benchmarks;

[MemoryDiagnoser]
public class EventStoreClientWriteBenchmarks
{
    private const string StreamId = "test-stream";

    private static readonly JsonUtf8BytesObjectSerde EventSerde = new();
    private static readonly JsonUtf8BytesMetadataSerde MetadataSerde = new();

    private FakeKurrentDBEventStoreClient<IDomainEvent> _client = null!;
    private AppendEvent<IDomainEvent>[] _appendEvents = null!;

    //[Params(100, 1_000, 10_000)]
    [Params(10_000)]
    public int EventCount { get; set; }

    [Params(false, true)]
    public bool SharedMetadataBuffer { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var consumer = new Consumer();

        var typeMap = new EventTypeMapBuilder()
            .MapType<TestEvent>()
            .Build();

        var codecOptions = new EventCodecOptions<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = typeMap,
            EventSerde = EventSerde,
            MetadataSerde = MetadataSerde,
            MetadataContributors = [new MetadataContributor(EventCount)]
        };

        var eventCodec = EventCodec.Create(codecOptions);

        _client = new FakeKurrentDBEventStoreClient<IDomainEvent>(eventCodec, consumer, SharedMetadataBuffer);

        _appendEvents = CreateAppendEvents(EventCount);
    }

    [Benchmark]
    public Task AppendToStreamAsync_NoIO()
    {
        return _client.AppendToStreamAsync(StreamId, _appendEvents);
    }

    private static AppendEvent<IDomainEvent>[] CreateAppendEvents(int count)
    {
        var events = new AppendEvent<IDomainEvent>[count];

        for (var i = 0; i < count; i++)
        {
            events[i] = new AppendEvent<IDomainEvent>(
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

    private sealed class FakeKurrentDBEventStoreClient<TEvent>(
        EventCodec<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventCodec,
        Consumer consumer,
        bool sharedMetadataBuffer) :
        IEventStoreClient<TEvent>
        where TEvent : notnull
    {
        public Task AppendToStreamAsync(
            string streamId,
            IEnumerable<AppendEvent<TEvent>> events,
            AppendToStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (sharedMetadataBuffer)
            {
                var encodingSession = eventCodec.Encoder.CreateSession();

                foreach (var e in events)
                {
                    var (eventName, eventData, metadata) = encodingSession.Encode(e);
                    consumer.Consume(eventName);
                    consumer.Consume(eventData);
                    consumer.Consume(metadata);
                }
            }
            else
            {
                foreach (var e in events)
                {
                    var (eventName, eventData, metadata) = eventCodec.Encoder.CreateSession().Encode(e);
                    consumer.Consume(eventName);
                    consumer.Consume(eventData);
                    consumer.Consume(metadata);
                }
            }

            return Task.CompletedTask;
        }

        public IAsyncEnumerable<ReadEvent<TEvent>> ReadStreamAsync(
            string streamId,
            ReadStreamOptions? options = null,
            CancellationToken cancellationToken = default)
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