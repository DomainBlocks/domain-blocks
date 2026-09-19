using BenchmarkDotNet.Attributes;

namespace DomainBlocks.EventStore.Benchmarks;

/// <summary>
/// Measures the read path without I/O for each serialization format: type mapping, payload and metadata
/// deserialization, and creation of each read event and its context, for <see cref="EventCount"/> events per
/// operation.
/// </summary>
[MemoryDiagnoser]
public class EventStoreReadBenchmarks
{
    private const string StreamId = "test-stream";

    private IEventStore<IDomainEvent, string, StreamPosition, LogPosition> _eventStore = null!;
    private ReadStreamOptions _readStreamOptions = null!;

    [ParamsAllValues]
    public SerializationFormat Format { get; set; }

    /// <summary>
    /// Every stored event has metadata of equivalent size to its payload. When <see langword="false"/>, reads exclude
    /// it, so it is never deserialized.
    /// </summary>
    [Params(false, true)]
    public bool IncludeMetadata { get; set; }

    //[Params(100, 1_000, 10_000)]
    [Params(10_000)]
    public int EventCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var storedEvents = TestEvents.Create(Format, EventCount, metadataEntryCount: 2);

        _eventStore = NoIOEventStore.Create(Format, storedEvents);
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
}