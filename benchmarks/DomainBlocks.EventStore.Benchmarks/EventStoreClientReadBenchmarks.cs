using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Events;
using DomainBlocks.Serialization.SystemTextJson;
using KurrentDB.Client;

namespace DomainBlocks.EventStore.Benchmarks;

[MemoryDiagnoser]
public class EventStoreClientReadBenchmarks
{
    private const string StreamId = "test-stream";

    private static readonly SystemTextJsonBytesSerializer EventSerializer = new();
    private static readonly SystemTextJsonBytesMetadataSerializer MetadataSerializer = new();

    private IEventStoreClient<IDomainEvent> _client = null!;
    private ReadStreamOptions _readStreamOptions = null!;

    [Params(false, true)]
    public bool IncludeMetadata { get; set; }

    //[Params(100, 1_000, 10_000)]
    [Params(10_000)]
    public uint EventCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var kurrentEvents = CreateKurrentEvents(EventCount);

        var typeMap = new EventTypeMapBuilder()
            .MapType<TestEvent>()
            .Build();

        var clientOptions = new EventStoreClientOptions<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            ConnectionProvider = new FakeKurrentDBEventStoreConnectionProvider(kurrentEvents),
            TypeMap = typeMap,
            EventSerializer = EventSerializer,
            MetadataSerializer = MetadataSerializer
        };

        _client = new EventStoreClient<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>(clientOptions);

        _readStreamOptions = new ReadStreamOptions { IncludeMetadata = IncludeMetadata };
    }

    [Benchmark]
    public async Task ReadStreamAsync_NoIO()
    {
        // Force enumeration
        await foreach (var _ in _client.ReadStreamAsync(StreamId, _readStreamOptions))
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

            var metadata = new Dictionary<string, string>
            {
                { "Value1", $"value1-{i}" },
                { "Value2", $"value2-{i}" },
            };

            var serializedEvent = EventSerializer.Serialize(@event);
            var serializedMetadata = MetadataSerializer.Serialize(metadata);

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

    private interface IDomainEvent;

    private sealed class TestEvent : IDomainEvent
    {
        public required string Value1 { get; init; }
        public required string Value2 { get; init; }
    }

    private sealed class FakeKurrentDBEventStoreConnectionProvider(ResolvedEvent[] kurrentEvents) :
        IEventStoreConnectionProvider<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
    {
        public ValueTask<ConnectionScope<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> AcquireAsync(
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(ConnectionScope.Create(new FakeKurrentDBEventStoreConnection(kurrentEvents)));
        }
    }

    private sealed class FakeKurrentDBEventStoreConnection(ResolvedEvent[] kurrentEvents) :
        IEventStoreConnection<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
    {
        public Task AppendToStreamAsync(
            string streamId,
            IEnumerable<AppendEvent<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> events,
            AppendToStreamOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async IAsyncEnumerable<ReadEvent<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>> ReadStreamAsync(
#pragma warning restore CS1998
            string streamId,
            ReadStreamOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var resolvedEvent in kurrentEvents)
            {
                var @event = resolvedEvent.Event;
                var originalEvent = resolvedEvent.OriginalEvent;

                var context = new ReadEventContext(
                    streamId,
                    new StreamVersion(originalEvent.EventNumber.ToUInt64()),
                    @event.Created,
                    new GlobalPosition(originalEvent.Position.CommitPosition));

                yield return Abstractions.Events.ReadEvent.Create(
                    @event.EventType,
                    @event.Data,
                    @event.Metadata,
                    context);
            }
        }
    }
}