using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Metadata;

namespace DomainBlocks.EventStore.Benchmarks;

/// <summary>
/// Measures the append path without I/O for each serialization format: contract/type mapping, payload serialization,
/// metadata contribution and metadata serialization for <see cref="EventCount"/> events per operation.
/// </summary>
[MemoryDiagnoser]
public class EventStoreWriteBenchmarks
{
    private const string StreamId = "test-stream";

    private IEventStore<IDomainEvent, string, StreamPosition, LogPosition> _eventStore = null!;
    private AppendableEvent<IDomainEvent>[] _appendEvents = null!;

    [ParamsAllValues]
    public SerializationFormat Format { get; set; }

    /// <summary>
    /// When <see langword="true"/>, every event carries one explicit metadata entry and one metadata contributor adds
    /// a second, so the metadata merge and serialization paths are exercised. When <see langword="false"/>, events
    /// carry no metadata and no contributors are configured.
    /// </summary>
    [Params(false, true)]
    public bool WithMetadata { get; set; }

    //[Params(100, 1_000, 10_000)]
    [Params(10_000)]
    public int EventCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var store = NoIOEventStore.Create(Format);

        _eventStore = WithMetadata
            ? store.WithMetadataContributors(new MetadataContributor(EventCount))
            : store;

        _appendEvents = TestEvents.Create(Format, EventCount, metadataEntryCount: WithMetadata ? 1 : 0);
    }

    [Benchmark]
    public Task AppendAsync_NoIO()
    {
        return _eventStore.AppendAsync(StreamId, _appendEvents);
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