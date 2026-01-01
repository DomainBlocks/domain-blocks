using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using DomainBlocks.EventStore.Abstractions.Events;
using KurrentDB.Client;

namespace DomainBlocks.EventStore.Abstractions.Benchmarks;

[MemoryDiagnoser]
public class EventStoreConnectionBenchmarks
{
    private const uint EventCount = 10_000;
    private const string StreamId = "test-stream";

    private readonly Consumer _consumer = new();
    private IEventStoreConnection<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> _connection = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var nativeEvents = CreateKurrentDBEvents(EventCount);
        _connection = new FakeKurrentDBEventStoreConnection(nativeEvents);
    }

    [Benchmark]
    public async Task ReadStreamAsync_AbstractionCostWithoutIO()
    {
        await foreach (var e in _connection.ReadStreamAsync(StreamId))
        {
            _consumer.Consume(e.Header.StreamVersion.Value);
            _consumer.Consume(e.Header.EventName);
            _consumer.Consume(e.Value.Length);
        }
    }

    private static ResolvedEvent[] CreateKurrentDBEvents(uint count)
    {
        var data = new byte[256];
        Random.Shared.NextBytes(data);

        var resolvedEvents = new ResolvedEvent[count];

        var metadata = new Dictionary<string, string>
        {
            { "type", "TestEvent" },
            { "created", (DateTime.UtcNow - DateTime.UnixEpoch).Ticks.ToString() },
            { "content-type", "application/octet-stream" }
        };

        for (ulong i = 0; i < count; i++)
        {
            var eventRecord = new EventRecord(
                StreamId,
                Uuid.NewUuid(),
                StreamPosition.FromStreamRevision(i),
                new Position(i, i),
                metadata,
                data,
                ReadOnlyMemory<byte>.Empty);

            resolvedEvents[i] = new ResolvedEvent(eventRecord, null, i);
        }

        return resolvedEvents;
    }

    private sealed class FakeKurrentDBEventStoreConnection(ResolvedEvent[] nativeEvents) :
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
        public async IAsyncEnumerable<ReadEvent<ReadOnlyMemory<byte>>> ReadStreamAsync(
#pragma warning restore CS1998
            string streamId,
            ReadStreamOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var resolvedEvent in nativeEvents)
            {
                var @event = resolvedEvent.Event;
                var originalEvent = resolvedEvent.OriginalEvent;

                var header = new ReadEventHeader(
                    streamId,
                    new StreamVersion(originalEvent.EventNumber.ToUInt64()),
                    @event.EventType,
                    FrozenDictionary<string, string>.Empty,
                    @event.Created.Date,
                    GlobalPosition.FromUInt64(originalEvent.Position.CommitPosition));

                yield return ReadEvent.Create(header, @event.Data);
            }
        }
    }
}