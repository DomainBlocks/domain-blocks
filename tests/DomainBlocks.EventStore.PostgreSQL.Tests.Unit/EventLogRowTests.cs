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
    public void DecodedEvent_FirstRequest_DecodesEventWithItsContext()
    {
        var row = new EventLogRow<string>(new CountingDecoder());
        const string metadata = """{"tenant": "acme"}""";
        row.Set(7, "order-1", 3, "OrderPlaced", PostgresEventData.FromJson("payload"), metadata, CreatedAt);

        var e = row.DecodedEvent;

        e.Payload.ShouldBe("OrderPlaced:payload");
        e.Context.LogPosition.ShouldBe(LogPosition.FromInt64(7));
        e.Context.StreamId.ShouldBe("order-1");
        e.Context.StreamPosition.ShouldBe(StreamPosition.FromInt64(3));
        e.Context.EventName.ShouldBe("OrderPlaced");
        e.Context.CreatedAt.ShouldBe(CreatedAt);
        e.Context.Metadata["raw"].ShouldBe(metadata);
    }

    [Test]
    public void DecodedEvent_RequestedSeveralTimes_DecodesOnce()
    {
        var decoder = new CountingDecoder();
        var row = CreateRow(null, decoder);

        var first = row.DecodedEvent;
        var second = row.DecodedEvent;
        var third = row.DecodedEvent;

        decoder.DecodeCount.ShouldBe(1);
        second.Payload.ShouldBeSameAs(first.Payload);
        third.Payload.ShouldBeSameAs(first.Payload);
    }

    [Test]
    public void DecodedEvent_NeverRequested_NeverDecodes()
    {
        var decoder = new CountingDecoder();
        var row = CreateRow(null, decoder);

        row.Set(8, "order-1", 4, "OrderShipped", PostgresEventData.FromJson("{}"), null, CreatedAt);

        decoder.DecodeCount.ShouldBe(0);
    }

    [Test]
    public void DecodedEvent_AfterRowIsSetAgain_DecodesNewEvent()
    {
        var decoder = new CountingDecoder();
        var row = new EventLogRow<string>(decoder);
        row.Set(7, "order-1", 3, "OrderPlaced", PostgresEventData.FromJson("first"), null, CreatedAt);
        _ = row.DecodedEvent;

        row.Set(8, "order-1", 4, "OrderShipped", PostgresEventData.FromJson("second"), null, CreatedAt);
        var e = row.DecodedEvent;

        e.Payload.ShouldBe("OrderShipped:second");
        e.Context.LogPosition.ShouldBe(LogPosition.FromInt64(8));
        decoder.DecodeCount.ShouldBe(2);
    }

    [Test]
    public void DecodedEvent_WhenDecodingFails_ThrowsSameExceptionToEveryoneAndDecodesOnce()
    {
        var decoder = new CountingDecoder { Failure = new InvalidOperationException("Cannot decode.") };
        var row = CreateRow(null, decoder);

        var first = Should.Throw<InvalidOperationException>(() => row.DecodedEvent);
        var second = Should.Throw<InvalidOperationException>(() => row.DecodedEvent);

        first.ShouldBeSameAs(decoder.Failure);
        second.ShouldBeSameAs(decoder.Failure);
        decoder.DecodeCount.ShouldBe(1);
    }

    [Test]
    public void DecodedEvent_AfterFailedRowIsSetAgain_DecodesNewEvent()
    {
        var decoder = new CountingDecoder { Failure = new InvalidOperationException("Cannot decode.") };
        var row = CreateRow(null, decoder);
        Should.Throw<InvalidOperationException>(() => row.DecodedEvent);

        decoder.Failure = null;
        row.Set(8, "order-1", 4, "OrderShipped", PostgresEventData.FromJson("second"), null, CreatedAt);

        row.DecodedEvent.Payload.ShouldBe("OrderShipped:second");
    }

    private static EventLogRow<string> CreateRow(string? metadata, CountingDecoder? decoder = null)
    {
        var row = new EventLogRow<string>(decoder ?? new CountingDecoder());
        row.Set(7, "order-1", 3, "OrderPlaced", PostgresEventData.FromJson("{}"), metadata, CreatedAt);

        return row;
    }
}