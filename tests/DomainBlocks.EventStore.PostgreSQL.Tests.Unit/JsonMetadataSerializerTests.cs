using System.Text.Encodings.Web;
using System.Text.Json;
using DomainBlocks.Serialization.SystemTextJson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class JsonMetadataSerializerTests
{
    [Test]
    public void Serialize_ThenDeserialize_RoundTrips()
    {
        var serializer = new JsonMetadataSerializer();
        KeyValuePair<string, string>[] metadata = [new("tenant", "acme"), new("user", "bob")];

        var json = serializer.Serialize(metadata);
        var result = serializer.Deserialize(json);

        result.ShouldBe(metadata);
    }

    [Test]
    public void Serialize_WritesFlatJsonObject()
    {
        var serializer = new JsonMetadataSerializer();
        KeyValuePair<string, string>[] metadata = [new("tenant", "acme"), new("quote", "a\"b")];

        serializer.Serialize(metadata).ShouldBe("{\"tenant\":\"acme\",\"quote\":\"a\\u0022b\"}");
    }

    [Test]
    public void Serialize_EmptySpan_WritesEmptyObject()
    {
        new JsonMetadataSerializer().Serialize([]).ShouldBe("{}");
    }

    [Test]
    public void Serialize_HonoursEncoderOption()
    {
        var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        KeyValuePair<string, string>[] metadata = [new("quote", "a\"b")];

        new JsonMetadataSerializer(options).Serialize(metadata).ShouldBe("{\"quote\":\"a\\\"b\"}");
    }

    [Test]
    public void Serialize_CalledRepeatedly_ReturnsIndependentStrings()
    {
        var serializer = new JsonMetadataSerializer();

        var first = serializer.Serialize([new("k", "first")]);
        var second = serializer.Serialize([new("k", "second-longer")]);

        first.ShouldBe("{\"k\":\"first\"}");
        second.ShouldBe("{\"k\":\"second-longer\"}");
    }

    [TestCase("")]
    [TestCase(null)]
    public void Deserialize_EmptyOrNull_ReturnsEmpty(string? json)
    {
        new JsonMetadataSerializer().Deserialize(json!).ShouldBeEmpty();
    }
}