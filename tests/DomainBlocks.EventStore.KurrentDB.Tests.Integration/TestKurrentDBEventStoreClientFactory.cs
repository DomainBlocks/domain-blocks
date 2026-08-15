using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.Abstractions.Codecs;
using DomainBlocks.EventStore.Codecs;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing.Integration;
using KurrentDB.Client;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

public sealed class TestKurrentDBEventStoreClientFactory<TEvent> :
    ITestEventStoreClientFactory<TEvent>
    where TEvent : notnull
{
    private readonly KurrentDBClient _kurrentClient;
    private readonly EventCodec<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> _codec;

    public TestKurrentDBEventStoreClientFactory(string connectionString, EventTypeMap eventTypeMap)
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

    public Task<ITestEventStoreClientHandle<TEvent>> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ITestEventStoreClientHandle<TEvent>>(new ClientHandle(_kurrentClient, _codec));
    }

    public ValueTask DisposeAsync() => _kurrentClient.DisposeAsync();

    private sealed class ClientHandle(
        KurrentDBClient kurrentDBClient,
        EventCodec<TEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> codec) :
        ITestEventStoreClientHandle<TEvent>
    {
        public IEventStoreClient<TEvent> Client { get; } =
            new KurrentDBEventStoreClient<TEvent>(kurrentDBClient, codec.Encoder, codec.Decoder);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}