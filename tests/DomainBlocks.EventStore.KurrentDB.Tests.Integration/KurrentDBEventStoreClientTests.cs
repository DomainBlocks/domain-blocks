using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.SystemTextJson;
using DomainBlocks.Testing.Integration;
using KurrentDB.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

[TestFixture]
public class KurrentDBEventStoreClientTests : EventStoreClientTests
{
    private KurrentDBClient _kurrentClient = null!;
    private KurrentDBEventStoreClient<IDomainEvent> _client = null!;

    protected override IEventStoreClient<IDomainEvent> Client => _client;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var eventTypeMap = EventTypeMap.Create(x => x.MapType<TestEvent>());

        var codecOptions = new EventCodecOptions<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = eventTypeMap,
            EventSerde = new JsonUtf8BytesObjectSerde(),
            MetadataSerde = new JsonUtf8BytesMetadataSerde()
        };

        var codec = EventCodec.Create(codecOptions);

        const string connectionString = "kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";

        _kurrentClient = new KurrentDBClient(KurrentDBClientSettings.Create(connectionString));

        _client = new KurrentDBEventStoreClient<IDomainEvent>(_kurrentClient, codec.Encoder, codec.Decoder);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _kurrentClient.DisposeAsync();
    }
}