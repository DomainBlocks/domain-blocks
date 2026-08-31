using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing.Integration;
using KurrentDB.Client;
using StreamPosition = KurrentDB.Client.StreamPosition;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

public sealed class TestKurrentDBEventStoreFactory<TEvent> :
    ITestEventStoreFactory<TEvent, string, StreamPosition, Position>
    where TEvent : notnull
{
    private readonly KurrentDBClient _kurrentClient;
    private readonly EventCodec<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> _codec;

    public TestKurrentDBEventStoreFactory(string connectionString, EventTypeMap eventTypeMap)
    {
        _kurrentClient = new KurrentDBClient(KurrentDBClientSettings.Create(connectionString));

        var codecOptions = new EventCodecOptions<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = eventTypeMap,
            EventSerde = new JsonUtf8BytesObjectSerde(),
            MetadataSerde = new JsonUtf8BytesMetadataSerde()
        };

        _codec = EventCodec.Create(codecOptions);
    }

    public Task<ITestEventStoreHandle<TEvent, string, StreamPosition, Position>> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ITestEventStoreHandle<TEvent, string, StreamPosition, Position>>(
            new EventStoreHandle(_kurrentClient, _codec));
    }

    public ValueTask DisposeAsync() => _kurrentClient.DisposeAsync();

    private sealed class EventStoreHandle(
        KurrentDBClient kurrentDBClient,
        EventCodec<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> codec) :
        ITestEventStoreHandle<TEvent, string, StreamPosition, Position>
    {
        public IEventStore<TEvent, string, StreamPosition, Position> Instance { get; } =
            new KurrentDBEventStore<TEvent>(kurrentDBClient, codec);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}