using DomainBlocks.Core.Serialization;
using DomainBlocks.EventStore.Serialization;
using EventStore.Client;
using NUnit.Framework;

namespace DomainBlocks.EventStore.Tests.Serialization;

[TestFixture]
public class EventStoreEventAdapterTests
{
    [Test]
    public void DeserializeMetadataReturnsEmptyDictionaryWhenPayloadIsEmpty()
    {
        var eventAdapter = new EventStoreEventAdapter(new JsonBytesEventDataSerializer());

        var eventRecord = new EventRecord(
            "stream1",
            Uuid.NewUuid(),
            StreamPosition.Start,
            Position.Start,
            new Dictionary<string, string>
            {
                { "type", "TestEvent" },
                { "created", DateTimeOffset.UtcNow.Ticks.ToString() },
                { "content-type", "application/json" }
            },
            ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty);

        var resolvedEvent = new ResolvedEvent(eventRecord, null, null);
        var metadata = eventAdapter.DeserializeMetadata(resolvedEvent);

        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata, Is.Empty);
    }
}