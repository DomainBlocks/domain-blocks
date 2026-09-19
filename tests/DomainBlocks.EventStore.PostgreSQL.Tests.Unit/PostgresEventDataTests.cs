using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class PostgresEventDataTests
{
    [Test]
    public void FromJson_Json_IsStringCase()
    {
        var data = PostgresEventData.FromJson("{\"a\":1}");

        (data is string).ShouldBeTrue();
        (data is ReadOnlyMemory<byte>).ShouldBeFalse();
        data.Json.ShouldBe("{\"a\":1}");
    }

    [Test]
    public void FromBytes_Bytes_IsMemoryCase()
    {
        var data = PostgresEventData.FromBytes(new byte[] { 1, 2, 3 });

        (data is ReadOnlyMemory<byte>).ShouldBeTrue();
        (data is string).ShouldBeFalse();
        data.Bytes.ToArray().ShouldBe([1, 2, 3]);
    }

    [Test]
    public void FromBytes_EmptyBytes_IsMemoryCase()
    {
        var data = PostgresEventData.FromBytes(ReadOnlyMemory<byte>.Empty);

        (data is ReadOnlyMemory<byte>).ShouldBeTrue();
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
    public void Default_NoData_IsNeitherCase()
    {
        var data = default(PostgresEventData);

        (data is string).ShouldBeFalse();
        (data is ReadOnlyMemory<byte>).ShouldBeFalse();
    }

    [Test]
    public void FromJson_Null_Throws()
    {
        Should.Throw<ArgumentNullException>(() => PostgresEventData.FromJson(null!));
    }

    [Test]
    public void Constructor_NullJson_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new PostgresEventData((string)null!));
    }

    [Test]
    public void Value_JsonData_ReturnsJson()
    {
        var data = new PostgresEventData("{\"a\":1}");

        data.HasValue.ShouldBeTrue();
        data.Value.ShouldBe("{\"a\":1}");
    }

    [Test]
    public void Value_BytesData_ReturnsBytes()
    {
        var data = new PostgresEventData(new byte[] { 1, 2, 3 });

        data.HasValue.ShouldBeTrue();
        data.Value.ShouldBeOfType<ReadOnlyMemory<byte>>().ToArray().ShouldBe([1, 2, 3]);
    }

    [Test]
    public void Value_EmptyBytesData_ReturnsBytes()
    {
        var data = new PostgresEventData(ReadOnlyMemory<byte>.Empty);

        data.HasValue.ShouldBeTrue();
        data.Value.ShouldBeOfType<ReadOnlyMemory<byte>>().Length.ShouldBe(0);
    }

    [Test]
    public void Value_DefaultData_ReturnsNull()
    {
        var data = default(PostgresEventData);

        data.HasValue.ShouldBeFalse();
        data.Value.ShouldBeNull();
    }

    [Test]
    public void TryGetValue_JsonData_ReturnsOnlyJson()
    {
        var data = new PostgresEventData("{}");

        data.TryGetValue(out string? json).ShouldBeTrue();
        json.ShouldBe("{}");
        data.TryGetValue(out ReadOnlyMemory<byte> _).ShouldBeFalse();
    }

    [Test]
    public void TryGetValue_BytesData_ReturnsOnlyBytes()
    {
        var data = new PostgresEventData(new byte[] { 1, 2, 3 });

        data.TryGetValue(out ReadOnlyMemory<byte> bytes).ShouldBeTrue();
        bytes.ToArray().ShouldBe([1, 2, 3]);
        data.TryGetValue(out string? _).ShouldBeFalse();
    }

    [Test]
    public void TryGetValue_DefaultData_ReturnsNeither()
    {
        var data = default(PostgresEventData);

        data.TryGetValue(out string? _).ShouldBeFalse();
        data.TryGetValue(out ReadOnlyMemory<byte> _).ShouldBeFalse();
    }

    [Test]
    public void Conversion_FromString_CreatesJsonData()
    {
        PostgresEventData data = "{\"a\":1}";

        (data is string).ShouldBeTrue();
        data.Json.ShouldBe("{\"a\":1}");
    }

    [Test]
    public void Conversion_FromMemory_CreatesBytesData()
    {
        PostgresEventData data = new ReadOnlyMemory<byte>([1, 2, 3]);

        (data is ReadOnlyMemory<byte>).ShouldBeTrue();
        data.Bytes.ToArray().ShouldBe([1, 2, 3]);
    }

    [Test]
    public void Switch_JsonData_MatchesStringCase()
    {
        Describe(PostgresEventData.FromJson("{}")).ShouldBe("json:{}");
    }

    [Test]
    public void Switch_BytesData_MatchesMemoryCase()
    {
        Describe(PostgresEventData.FromBytes(new byte[] { 1, 2, 3 })).ShouldBe("bytes:3");
    }

    [Test]
    public void Switch_DefaultData_MatchesNull()
    {
        Describe(default).ShouldBe("none");
    }

    [Test]
    public void IsNull_DefaultData_ReturnsTrue()
    {
        var data = default(PostgresEventData);

        (data is null).ShouldBeTrue();
    }

    // The compiler silently falls back to the boxing Value property if TryGetValue ever stops matching the
    // non-boxing access pattern, so only an allocation check can catch that.
    [Test]
    public void Switch_BytesData_DoesNotAllocate()
    {
        var data = PostgresEventData.FromBytes(new byte[] { 1, 2, 3 });
        LengthOf(data);

        var before = GC.GetAllocatedBytesForCurrentThread();
        long total = 0;

        for (var i = 0; i < 1_000; i++)
            total += LengthOf(data);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        total.ShouldBe(3_000);
        allocated.ShouldBe(0);
    }

    // Exhaustive without a discard: the compiler knows every case of the union.
    private static string Describe(PostgresEventData data)
    {
        return data switch
        {
            string json => $"json:{json}",
            ReadOnlyMemory<byte> bytes => $"bytes:{bytes.Length}",
            null => "none"
        };
    }

    private static int LengthOf(PostgresEventData data)
    {
        return data switch
        {
            string json => json.Length,
            ReadOnlyMemory<byte> bytes => bytes.Length,
            null => 0
        };
    }
}