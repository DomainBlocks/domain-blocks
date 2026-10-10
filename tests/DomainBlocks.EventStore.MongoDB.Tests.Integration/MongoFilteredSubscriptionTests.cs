using DomainBlocks.EventStore.Filtering;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Integration;

/// <summary>
/// MongoDB-specific filtered subscription behaviour beyond the shared suite: the metadata keys that a query cannot
/// address are refused at the call, before anything is read.
/// </summary>
public class MongoFilteredSubscriptionTests
{
    private readonly MongoEventStoreOptions _options = new() { DatabaseName = $"flt_{Guid.NewGuid():N}" };

    private IEventStore<object, string, StreamPosition, LogPosition> _eventStore = null!;

    [SetUp]
    public void SetUp()
    {
        _eventStore = new MongoEventStoreBuilder<object>()
            .UseClient(MongoTestEnvironment.MongoClient)
            .UseOptions(_options)
            .ConfigureCodec(x => x.MapEvent<TestEvent>())
            .Build();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _eventStore.DisposeAsync();
        await MongoTestEnvironment.MongoClient.DropDatabaseAsync(_options.DatabaseName);
    }

    [TestCase("a.b")]
    [TestCase("$a")]
    public void SubscribeToAll_WithFilterOnMetadataKeyThatQueryCannotAddress_ThrowsAtCall(string key)
    {
        var options = new SubscriptionOptions { Filter = EventFilter.MetadataExists(key) };

        Should.Throw<EventFilterNotSupportedException>(() => _eventStore.SubscribeToAll(options: options));
    }

    [TestCase("a.b")]
    [TestCase("$a")]
    public void SubscribeToStream_WithFilterOnMetadataKeyThatQueryCannotAddress_ThrowsAtCall(string key)
    {
        var options = new SubscriptionOptions { Filter = !EventFilter.Metadata(key, "value") };

        Should.Throw<EventFilterNotSupportedException>(() =>
            _eventStore.SubscribeToStream("stream-1", options: options));
    }
}