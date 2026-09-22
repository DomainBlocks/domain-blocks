using BenchmarkDotNet.Attributes;

namespace DomainBlocks.EventStore.Benchmarks;

/// <summary>
/// Measures a read that wants only some of the events, without I/O. The store has no filter, so every event is
/// decoded and the caller picks by event name. This is the cost a filter has to beat.
/// </summary>
[MemoryDiagnoser]
public class EventStoreFilteredReadBenchmarks
{
    private const string StreamId = "test-stream";

    private IEventStore<IDomainEvent, string, StreamPosition, LogPosition> _eventStore = null!;

    /// <summary>
    /// How many in every hundred events the read wants.
    /// </summary>
    [Params(1, 50, 100)]
    public int MatchPercent { get; set; }

    [Params(10_000)]
    public int EventCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var storedEvents = TestEvents.Create(
            SerializationFormat.Json,
            EventCount,
            metadataEntryCount: 2,
            testEventPercent: MatchPercent);

        _eventStore = NoIOEventStore.Create(SerializationFormat.Json, storedEvents);
    }

    [Benchmark]
    public async Task<int> ReadStream_NoIO_HandFiltered()
    {
        var count = 0;

        await foreach (var e in _eventStore.ReadStream(StreamId))
        {
            if (e.Context.EventName == nameof(TestEvent))
                count++;
        }

        return count;
    }
}