using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Transforms;

namespace DomainBlocks.EventStore.Benchmarks;

/// <summary>
/// Measures what the read transform stage adds to the read path without I/O, for <see cref="EventCount"/> events per
/// operation. The stage runs on decoded events, so its cost does not depend on the serialization format, and a single
/// format is used.
/// </summary>
[MemoryDiagnoser]
public class EventStoreReadTransformBenchmarks
{
    private const string StreamId = "test-stream";
    private const SerializationFormat Format = SerializationFormat.JsonUtf8;

    private IEventStore<IDomainEvent, string, StreamPosition, LogPosition> _eventStore = null!;

    /// <summary>
    /// <see cref="TransformMode.None"/> reads decoded events as-is. <see cref="TransformMode.Probe"/> registers a
    /// transform for a type that never occurs, so only the per-event lookup is paid. <see cref="TransformMode.FanOut"/>
    /// registers a transform that turns every event into two.
    /// </summary>
    [ParamsAllValues]
    public TransformMode Transforms { get; set; }

    //[Params(100, 1_000, 10_000)]
    [Params(10_000)]
    public int EventCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var storedEvents = TestEvents.Create(Format, EventCount, metadataEntryCount: 0);

        IReadEventTransform<IDomainEvent>[] transforms = Transforms switch
        {
            TransformMode.None => [],
            TransformMode.Probe => [new UnusedEventTransform()],
            TransformMode.FanOut => [new TestEventFanOutTransform()],
            _ => throw new ArgumentOutOfRangeException()
        };

        _eventStore = NoIOEventStore.Create(Format, storedEvents).WithReadTransforms(transforms);
    }

    [Benchmark]
    public async Task ReadStream_NoIO()
    {
        // Force enumeration
        await foreach (var _ in _eventStore.ReadStream(StreamId))
        {
        }
    }

    public enum TransformMode
    {
        None,
        Probe,
        FanOut
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
}