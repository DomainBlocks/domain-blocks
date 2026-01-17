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
        var codecOptions = new EventCodecOptions<IDomainEvent, ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>
        {
            TypeMap = new EventTypeMapBuilder().MapType<TestEvent>().Build(),
            EventSerializer = new SystemTextJsonBytesSerializer(),
            MetadataSerializer = new SystemTextJsonBytesMetadataSerializer()
        };

        var codec = EventCodec.Create(codecOptions);

        const string connectionString = "kurrentdb://admin:changeit@localhost:2113?tls=false&tlsVerifyCert=false";
        _kurrentClient = new KurrentDBClient(KurrentDBClientSettings.Create(connectionString));
        _client = new KurrentDBEventStoreClient<IDomainEvent>(_kurrentClient, codec);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _kurrentClient.DisposeAsync();
    }
}