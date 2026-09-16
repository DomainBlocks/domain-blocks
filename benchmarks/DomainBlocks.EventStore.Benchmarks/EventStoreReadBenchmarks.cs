using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.Transforms;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;
using KurrentDB.Client;

namespace DomainBlocks.EventStore.Benchmarks;

// Inside the namespace so that it shadows DomainBlocks.EventStore.StreamPosition from the parent namespace.
using StreamPosition = global::KurrentDB.Client.StreamPosition;

/// <summary>
/// Measures the read path without I/O: type mapping, payload and metadata deserialization, and optionally the read
/// transform stage, for <see cref="EventCount"/> events per operation.
/// </summary>
[MemoryDiagnoser]
public class EventStoreReadBenchmarks
{
    private const string StreamId = "test-stream";

    private static readonly JsonUtf8BytesObjectSerializer Utf8EventSerializer = new();
    private static readonly JsonUtf8BytesMetadataSerializer Utf8MetadataSerializer = new();

    private IEventStore<IDomainEvent, string, StreamPosition, Position> _eventStore = null!;
    private ReadStreamOptions _readStreamOptions = null!;

    [Params(false, true)]
    public bool IncludeMetadata { get; set; }

    /// <summary>
    /// <see cref="TransformMode.None"/> reads decoded events as-is. <see cref="TransformMode.Probe"/> registers a
    /// transform for a type that never occurs, so only the per-event lookup is paid. <see cref="TransformMode.FanOut"/>
    /// registers a transform that turns every event into two.
    /// </summary>
    [Params(TransformMode.None, TransformMode.Probe, TransformMode.FanOut)]
    public TransformMode Transforms { get; set; }

    //[Params(100, 1_000, 10_000)]
    [Params(10_000)]
    public uint EventCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var kurrentEvents = CreateKurrentEvents(EventCount);

        var codecOptions = new EventCodecOptions<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>()),
            EventSerializer = Utf8EventSerializer,
            MetadataSerializer = Utf8MetadataSerializer
        };

        var codec = EventCodec.Create(codecOptions);

        IReadEventTransform<IDomainEvent>[] transforms = Transforms switch
        {
            TransformMode.None => [],
            TransformMode.Probe => [new UnusedEventTransform()],
            TransformMode.FanOut => [new TestEventFanOutTransform()],
            _ => throw new ArgumentOutOfRangeException()
        };

        _eventStore = new FakeKurrentDBEventStore<IDomainEvent>(kurrentEvents, codec).WithReadTransforms(transforms);

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

            KeyValuePair<string, string>[] metadata =
            [
                KeyValuePair.Create("Value1", $"value1-{i}"),
                KeyValuePair.Create("Value2", $"value2-{i}")
            ];

            var serializedEvent = Utf8EventSerializer.Serialize(@event);
            var serializedMetadata = Utf8MetadataSerializer.Serialize(metadata);

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

    public enum TransformMode
    {
        None,
        Probe,
        FanOut
    }

    private interface IDomainEvent;

    private sealed class TestEvent : IDomainEvent
    {
        public required string Value1 { get; init; }
        public required string Value2 { get; init; }
    }

    private sealed class TestEventPart : IDomainEvent
    {
        public required string Value { get; init; }
    }

    private sealed class UnusedEvent : IDomainEvent;

    private sealed class UnusedEventTransform : ReadEventTransform<IDomainEvent, UnusedEvent>
    {
        protected override IEnumerable<IDomainEvent> Apply(UnusedEvent @event, ReadEventInfo info)
        {
            throw new InvalidOperationException("This transform should never be applied.");
        }
    }

    private sealed class TestEventFanOutTransform : ReadEventTransform<IDomainEvent, TestEvent>
    {
        protected override IEnumerable<IDomainEvent> Apply(TestEvent @event, ReadEventInfo info)
        {
            return [new TestEventPart { Value = @event.Value1 }, new TestEventPart { Value = @event.Value2 }];
        }
    }

    private sealed class FakeKurrentDBEventStore<TEvent>(
        ResolvedEvent[] kurrentEvents,
        IEventDecoder<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> eventDecoder) :
        IEventStore<TEvent, string, StreamPosition, Position>
        where TEvent : notnull
    {
        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

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

        public IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, Position>> SubscribeToAll(
            SubscriptionOrigin<Position>? origin = null,
            SubscriptionOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<SubscriptionMessage<TEvent, string, StreamPosition, Position>> SubscribeToStream(
            string streamId,
            SubscriptionOrigin<StreamPosition>? origin = null,
            SubscriptionOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}