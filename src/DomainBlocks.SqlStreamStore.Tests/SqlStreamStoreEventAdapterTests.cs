using DomainBlocks.Core.Serialization;
using DomainBlocks.Core.Serialization.SqlStreamStore;
using NUnit.Framework;
using SqlStreamStore.Streams;

namespace DomainBlocks.SqlStreamStore.Tests;

[TestFixture]
public class SqlStreamStoreEventAdapterTests
{
    [Test]
    public void DeserializeMetadataReturnsEmptyDictionaryWhenJsonIsNull()
    {
        var eventAdapter = new SqlStreamStoreEventAdapter(new JsonStringEventDataSerializer());
        string? metadataJson = null;
        var message = new StreamMessage("stream1", Guid.NewGuid(), 0, 0, DateTime.Now, "TestEvent", metadataJson, "{}");
        var metadata = eventAdapter.DeserializeMetadata(message);

        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata, Is.Empty);
    }
}