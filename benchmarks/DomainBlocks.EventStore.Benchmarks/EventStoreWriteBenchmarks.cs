using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Metadata;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;
using KurrentDB.Client;

namespace DomainBlocks.EventStore.Benchmarks;

// Inside the namespace so that it shadows DomainBlocks.EventStore.StreamPosition from the parent namespace.
using StreamPosition = global::KurrentDB.Client.StreamPosition;

/// <summary>
/// Measures the append path without I/O: contract/type mapping, payload serialization, metadata contribution and
/// metadata serialization for <see cref="EventCount"/> events per operation.
/// </summary>
[MemoryDiagnoser]
public class EventStoreWriteBenchmarks
{
    private const string StreamId = "test-stream";

    private static readonly JsonUtf8BytesObjectSerializer Utf8EventSerializer = new();
    private static readonly JsonUtf8BytesMetadataSerializer Utf8MetadataSerializer = new();

    private IEventStore<IDomainEvent, string, StreamPosition, Position> _eventStore = null!;
    private AppendableEvent<IDomainEvent>[] _appendEvents = null!;

    //[Params(100, 1_000, 10_000)]
    [Params(10_000)]
    public int EventCount { get; set; }

    /// <summary>
    /// When <see langword="true"/>, every event carries one explicit metadata entry and one metadata contributor adds
    /// a second, so the metadata merge and serialization paths are exercised. When <see langword="false"/>, events
    /// carry no metadata and no contributors are configured.
    /// </summary>
    [Params(false, true)]
    public bool WithMetadata { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var codecOptions = new EventCodecOptions<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>()),
            EventSerializer = Utf8EventSerializer,
            MetadataSerializer = Utf8MetadataSerializer
        };

        var codec = EventCodec.Create(codecOptions);
        var store = new FakeKurrentDBEventStore<IDomainEvent>(codec, new Consumer());

        _eventStore = WithMetadata
            ? store.WithMetadataContributors(new MetadataContributor(EventCount))
            : store;

        _appendEvents = CreateAppendEvents(EventCount, WithMetadata);
    }

    [Benchmark]
    public Task AppendAsync_NoIO()
    {
        return _eventStore.AppendAsync(StreamId, _appendEvents);
    }

    private static AppendableEvent<IDomainEvent>[] CreateAppendEvents(int count, bool withMetadata)
    {
        var events = new AppendableEvent<IDomainEvent>[count];

        for (var i = 0; i < count; i++)
        {
            var @event = new TestEvent
            {
                Value1 = $"value1-{i}",
                Value2 = $"value2-{i}"
            };

            events[i] = withMetadata
                ? new AppendableEvent<IDomainEvent>(@event, [KeyValuePair.Create("Value1", $"value1-{i}")])
                : new AppendableEvent<IDomainEvent>(@event);
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
        IEventEncoder<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventEncoder,
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
            foreach (var (eventName, eventData, metadata) in eventEncoder.Encode(events))
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
            SubscriptionOrigin<Position>? origin = null,
            SubscriptionOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<SubscriptionMessage> SubscribeToStream(
            string streamId,
            SubscriptionOrigin<StreamPosition>? origin = null,
            SubscriptionOptions? options = null)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class MetadataContributor(int eventCount) : IMetadataContributor<IDomainEvent>
    {
        private int _counter;

        public void Contribute(IDomainEvent @event, MetadataWriter metadata)
        {
            metadata.Set("Value2", $"value2-{_counter}");

            _counter++;
            if (_counter == eventCount)
                _counter = 0;
        }
    }
}