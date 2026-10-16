using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class EventLogRowTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Set_Row_ExposesStoredValuesToFilter()
    {
        var row = new EventLogRow<string>(new CountingDecoder());

        row.Set(7, "order-1", 3, "OrderPlaced", PostgresEventData.FromJson("{}"), null, CreatedAt);

        row.Position.ShouldBe(7);
        row.StreamId.ShouldBe("order-1");
        row.StreamPosition.ShouldBe(3);
        row.EventName.ShouldBe("OrderPlaced");
        row.CreatedAt.ShouldBe(CreatedAt);
    }

    [TestCase("""{"tenant": "acme", "region": "eu"}""", "tenant", "acme")]
    [TestCase("""{"tenant": "acme", "region": "eu"}""", "region", "eu")]
    [TestCase("""{"note": ""}""", "note", "")]
    [TestCase("""{"tenant": "o'brien \"and\" sons\\"}""", "tenant", "o'brien \"and\" sons\\")]
    [TestCase("""{"tenant": "café"}""", "tenant", "café")]
    [TestCase("""{"a\"b": "quoted key"}""", "a\"b", "quoted key")]
    [TestCase("""{"tenants": "plural", "tenant": "singular"}""", "tenant", "singular")]
    public void TryGetMetadata_KeyInStoredMetadata_GetsItsValue(string metadata, string key, string expected)
    {
        var row = CreateRow(metadata);

        row.TryGetMetadata(key, out var value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [TestCase("""{"tenant": "acme"}""", "Tenant")]
    [TestCase("""{"tenant": "acme"}""", "tenan")]
    [TestCase("""{"tenant": "acme"}""", "acme")]
    [TestCase("""{"outer": {"tenant": "acme"}}""", "tenant")]
    [TestCase("{}", "tenant")]
    [TestCase(null, "tenant")]
    public void TryGetMetadata_KeyNotInStoredMetadata_GetsNothing(string? metadata, string key)
    {
        var row = CreateRow(metadata);

        row.TryGetMetadata(key, out var value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [TestCase("""{"count": 12}""", "12")]
    [TestCase("""{"count": true}""", "true")]
    [TestCase("""{"count": {"a": 1}}""", """{"a": 1}""")]
    public void TryGetMetadata_ValueThatIsNotAString_GetsItAsWritten(string metadata, string expected)
    {
        var row = CreateRow(metadata);

        row.TryGetMetadata("count", out var value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [Test]
    public void TryGetMetadata_Row_DoesNotDecodeEvent()
    {
        var decoder = new CountingDecoder();
        var row = CreateRow("""{"tenant": "acme"}""", decoder);

        row.TryGetMetadata("tenant", out _);

        decoder.DecodeCount.ShouldBe(0);
    }

    [Test]
    public void Payload_FirstRequest_DecodesPayloadAndMetadata()
    {
        const string metadata = """{"tenant": "acme"}""";
        var row = CreateRow(metadata);

        row.Payload.ShouldBe("OrderPlaced:{}");
        row.Metadata["raw"].ShouldBe(metadata);
    }

    [Test]
    public void Context_Row_ReadsContextWithoutDecoding()
    {
        var decoder = new CountingDecoder();
        var row = CreateRow(null, decoder);

        var context = row.Context;

        context.LogPosition.ShouldBe(LogPosition.FromInt64(7));
        context.StreamId.ShouldBe("order-1");
        context.StreamPosition.ShouldBe(StreamPosition.FromInt64(3));
        context.EventName.ShouldBe("OrderPlaced");
        context.CreatedAt.ShouldBe(CreatedAt);
        decoder.DecodeCount.ShouldBe(0);
    }

    [Test]
    public void PayloadAndMetadata_RequestedSeveralTimes_DecodeOnce()
    {
        var decoder = new CountingDecoder();
        var row = CreateRow(null, decoder);

        var first = row.Payload;
        var metadata = row.Metadata;
        var second = row.Payload;

        decoder.DecodeCount.ShouldBe(1);
        second.ShouldBeSameAs(first);
        row.Metadata.ShouldBeSameAs(metadata);
    }

    [Test]
    public void Payload_NeverRequested_NeverDecodes()
    {
        var decoder = new CountingDecoder();
        var row = CreateRow(null, decoder);

        row.Set(8, "order-1", 4, "OrderShipped", PostgresEventData.FromJson("{}"), null, CreatedAt);

        decoder.DecodeCount.ShouldBe(0);
    }

    [Test]
    public void Payload_AfterRowIsSetAgain_DecodesNewEvent()
    {
        var decoder = new CountingDecoder();
        var row = new EventLogRow<string>(decoder);
        row.Set(7, "order-1", 3, "OrderPlaced", PostgresEventData.FromJson("first"), null, CreatedAt);
        _ = row.Payload;

        row.Set(8, "order-1", 4, "OrderShipped", PostgresEventData.FromJson("second"), null, CreatedAt);

        row.Payload.ShouldBe("OrderShipped:second");
        row.Context.LogPosition.ShouldBe(LogPosition.FromInt64(8));
        decoder.DecodeCount.ShouldBe(2);
    }

    [Test]
    public void PayloadAndMetadata_WhenDecodingFails_ThrowSameExceptionToEveryoneAndDecodeOnce()
    {
        var decoder = new CountingDecoder { Failure = new InvalidOperationException("Cannot decode.") };
        var row = CreateRow(null, decoder);

        var first = Should.Throw<InvalidOperationException>(() => row.Payload);
        var second = Should.Throw<InvalidOperationException>(() => row.Metadata);

        first.ShouldBeSameAs(decoder.Failure);
        second.ShouldBeSameAs(decoder.Failure);
        decoder.DecodeCount.ShouldBe(1);
    }

    [Test]
    public void Payload_AfterFailedRowIsSetAgain_DecodesNewEvent()
    {
        var decoder = new CountingDecoder { Failure = new InvalidOperationException("Cannot decode.") };
        var row = CreateRow(null, decoder);
        Should.Throw<InvalidOperationException>(() => row.Payload);

        decoder.Failure = null;
        row.Set(8, "order-1", 4, "OrderShipped", PostgresEventData.FromJson("second"), null, CreatedAt);

        row.Payload.ShouldBe("OrderShipped:second");
    }

    private static EventLogRow<string> CreateRow(string? metadata, CountingDecoder? decoder = null)
    {
        var row = new EventLogRow<string>(decoder ?? new CountingDecoder());
        row.Set(7, "order-1", 3, "OrderPlaced", PostgresEventData.FromJson("{}"), metadata, CreatedAt);

        return row;
    }
}