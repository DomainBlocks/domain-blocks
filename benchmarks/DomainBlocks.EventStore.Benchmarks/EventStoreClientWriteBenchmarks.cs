using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Metadata;
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

        _client = new FakeKurrentDBEventStoreClient<IDomainEvent>(eventCodec, consumer);

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
        Consumer consumer) :
        IEventStoreClient<TEvent>
        where TEvent : notnull
    {
        public Task AppendToStreamAsync(
            string streamId,
            IEnumerable<AppendEvent<TEvent>> events,
            AppendToStreamOptions? options = null,
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

        public IAsyncEnumerable<ReadEvent<TEvent>> ReadAll(ReadAllOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<ReadEvent<TEvent>> ReadStream(string streamId, ReadStreamOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(SubscribeToAllOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
            string streamId,
            SubscribeToStreamOptions? options = null)
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