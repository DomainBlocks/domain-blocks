using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using DomainBlocks.Serialization.SystemTextJson;

namespace DomainBlocks.EventStore.Benchmarks;

[MemoryDiagnoser]
public class EventStoreClientBenchmarks
{
    private const int EventCount = 10_000;

    private IEventStoreClient<IDomainEvent> _client = null!;
    private AppendEvent<IDomainEvent>[] _eventsToAppend = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var typeMap = new EventTypeMapBuilder()
            .MapType<TestEvent>()
            .Build();

        IEventStoreClientAdapter<byte[], byte[]> adapter = new FakeEventStoreClientAdapter();

        var clientOptions = new EventStoreClientOptions<IDomainEvent, byte[], byte[]>
        {
            AdapterFactory = _ => ValueTask.FromResult(adapter),
            TypeMap = typeMap,
            EventSerializer = new SystemTextJsonBytesSerializer(),
            MetadataSerializer = new SystemTextJsonBytesMetadataSerializer(),
            MetadataContributors = [new MetadataContributor()]
        };

        _client = new EventStoreClient<IDomainEvent, byte[], byte[]>(clientOptions);
        _eventsToAppend = CreateEventsToAppend(EventCount);
    }

    [Benchmark]
    public Task AppendToStreamAsync_AbstractionCostWithoutIO()
    {
        return _client.AppendToStreamAsync("test-stream", _eventsToAppend);
    }

    private static AppendEvent<IDomainEvent>[] CreateEventsToAppend(int count)
    {
        var events = new AppendEvent<IDomainEvent>[count];

        for (var i = 0; i < count; i++)
        {
            events[i] = new AppendEvent<IDomainEvent>(
                new TestEvent { Value = $"value-{i}" },
                [KeyValuePair.Create("Index", i.ToString())]);
        }

        return events;
    }

    private interface IDomainEvent;

    private sealed class TestEvent : IDomainEvent
    {
        public required string Value { get; init; }
    }

    private sealed class FakeEventStoreClientAdapter : IEventStoreClientAdapter<byte[], byte[]>
    {
        public Task AppendToStreamAsync(
            string streamId,
            IEnumerable<AppendEvent<byte[], byte[]>> events,
            AppendToStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            foreach (var _ in events)
            {
            }

            return Task.CompletedTask;
        }

        public IAsyncEnumerable<ReadEvent<byte[]>> ReadStreamAsync(
            string streamId,
            ReadStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class MetadataContributor : IMetadataContributor<IDomainEvent>
    {
        private const string EventClrTypeKey = "EventClrType";

        public void Contribute(IDomainEvent @event, object? contract, string eventName, MetadataWriter metadata)
        {
            metadata.Set(EventClrTypeKey, (contract ?? @event).GetType().Name);
        }
    }
}