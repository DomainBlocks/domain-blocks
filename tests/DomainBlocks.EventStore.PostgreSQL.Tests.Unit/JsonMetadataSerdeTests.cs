using DomainBlocks.Serialization.SystemTextJson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class JsonMetadataSerdeTests
{
    [Test]
    public void Serialize_ThenDeserialize_RoundTrips()
    {
        var serde = new JsonMetadataSerde();
        var metadata = new Dictionary<string, string> { ["tenant"] = "acme", ["user"] = "bob" };

        var json = serde.Serialize(metadata);
        var result = serde.Deserialize(json);

        result.ShouldBe(metadata);
    }

    [TestCase("")]
    [TestCase(null)]
    public void Deserialize_EmptyOrNull_ReturnsEmpty(string? json)
    {
        new JsonMetadataSerde().Deserialize(json!).ShouldBeEmpty();
    }
}
