using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using DomainBlocks.Serialization.SystemTextJson;

namespace DomainBlocks.EventStore.Benchmarks;

[MemoryDiagnoser]
public class EventStoreClientWriteBenchmarks
{
    private const int EventCount = 10_000;
    private const string StreamId = "test-stream";

    private IEventStoreClient<IDomainEvent> _client = null!;
    private AppendEvent<IDomainEvent>[] _appendEvents = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var typeMap = new EventTypeMapBuilder()
            .MapType<TestEvent>()
            .Build();

        var clientOptions = new EventStoreClientOptions<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            ConnectionProvider = new FakeEventStoreConnectionProvider(),
            TypeMap = typeMap,
            EventSerializer = new SystemTextJsonBytesSerializer(),
            MetadataSerializer = new SystemTextJsonBytesMetadataSerializer(),
            MetadataContributors = [new MetadataContributor()]
        };

        _client = new EventStoreClient<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>(clientOptions);
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

    private sealed class FakeEventStoreConnectionProvider :
        IEventStoreConnectionProvider<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
    {
        public ValueTask<ConnectionScope<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> AcquireAsync(
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(ConnectionScope.Create(new FakeKurrentDBEventStoreConnection()));
        }
    }

    private sealed class FakeKurrentDBEventStoreConnection :
        IEventStoreConnection<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
    {
        public Task AppendToStreamAsync(
            string streamId,
            IEnumerable<AppendEvent<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> events,
            AppendToStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            // Force enumeration
            foreach (var _ in events)
            {
            }

            return Task.CompletedTask;
        }

        public IAsyncEnumerable<ReadEvent<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> ReadStreamAsync(
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