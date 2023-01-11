using DomainBlocks.Core.Serialization;
using DomainBlocks.SqlStreamStore.Serialization;
using NUnit.Framework;
using SqlStreamStore.Streams;

namespace DomainBlocks.SqlStreamStore.Tests.Serialization;

[TestFixture]
public class SqlStreamStoreEventAdapterTests
{
    [TestCase(null)]
    [TestCase("")]
    public void DeserializeMetadataWithEmptyPayloadTestCases(string? metadataPayload)
    {
        var eventAdapter = new SqlStreamStoreEventAdapter(new JsonStringEventDataSerializer());
        var message = CreateStreamMessage(metadataPayload);
        var metadata = eventAdapter.DeserializeMetadata(message);
        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata, Is.Empty);
    }

    private static StreamMessage CreateStreamMessage(string? metadataPayload)
    {
        return new StreamMessage(
            "stream1",
            Guid.NewGuid(),
            0,
            0,
            DateTime.Now,
            "TestEvent",
            metadataPayload,
            "{}");
    }
}