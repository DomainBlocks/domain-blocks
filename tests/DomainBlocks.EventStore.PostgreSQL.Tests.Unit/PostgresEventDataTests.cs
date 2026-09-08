using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class PostgresEventDataTests
{
    [Test]
    public void FromJson_IsJson()
    {
        var data = PostgresEventData.FromJson("{\"a\":1}");

        data.IsJson.ShouldBeTrue();
        data.IsBytes.ShouldBeFalse();
        data.Json.ShouldBe("{\"a\":1}");
    }

    [Test]
    public void FromBytes_IsBytes()
    {
        var data = PostgresEventData.FromBytes(new byte[] { 1, 2, 3 });

        data.IsBytes.ShouldBeTrue();
        data.IsJson.ShouldBeFalse();
        data.Bytes.ToArray().ShouldBe([1, 2, 3]);
    }

    [Test]
    public void FromBytes_EmptyBytes_IsBytes()
    {
        var data = PostgresEventData.FromBytes(ReadOnlyMemory<byte>.Empty);

        data.IsBytes.ShouldBeTrue();
        data.Bytes.Length.ShouldBe(0);
    }

    [Test]
    public void Json_WhenBytes_Throws()
    {
        var data = PostgresEventData.FromBytes(new byte[] { 1 });
        Should.Throw<InvalidOperationException>(() => data.Json);
    }

    [Test]
    public void Bytes_WhenJson_Throws()
    {
        var data = PostgresEventData.FromJson("{}");
        Should.Throw<InvalidOperationException>(() => data.Bytes);
    }

    [Test]
    public void Default_IsNeitherJsonNorBytes()
    {
        var data = default(PostgresEventData);

        data.IsJson.ShouldBeFalse();
        data.IsBytes.ShouldBeFalse();
    }

    [Test]
    public void FromJson_Null_Throws()
    {
        Should.Throw<ArgumentNullException>(() => PostgresEventData.FromJson(null!));
    }
}
