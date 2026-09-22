using DomainBlocks.EventStore.Codecs;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Unit;

public class EventLogRowTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void GetEvent_WhenAskedTwice_DecodesOnce()
    {
        var decoder = new CountingDecoder();
        var row = CreateRow(decoder, metadata: """{"tenant":"acme"}""");

        var first = row.DecodedEvent;
        var second = row.DecodedEvent;

        decoder.DecodeCount.ShouldBe(1);
        second.Payload.ShouldBeSameAs(first.Payload);
        row.DecodedPayload.ShouldBeSameAs(first.Payload);
    }

    [Test]
    public void GetEvent_WhenDecoded_CarriesTheRow()
    {
        var e = CreateRow(new CountingDecoder(), metadata: """{"tenant":"acme"}""").DecodedEvent;

        e.Payload.ShouldBe("OrderPlaced:{}");
        e.Context.StreamId.ShouldBe("order-1");
        e.Context.EventName.ShouldBe("OrderPlaced");
        e.Context.StreamPosition.ShouldBe(StreamPosition.FromInt64(3));
        e.Context.LogPosition.ShouldBe(LogPosition.FromInt64(7));
        e.Context.CreatedAt.ShouldBe(CreatedAt);
        e.Context.Metadata.ShouldBe(new Dictionary<string, string> { ["raw"] = """{"tenant":"acme"}""" });
    }

    [Test]
    public void Set_WhenGivenAnotherRow_ForgetsTheEventOfTheLast()
    {
        var decoder = new CountingDecoder();
        var row = CreateRow(decoder);
        var first = row.DecodedEvent;

        row.Set(8, "order-2", 0, "OrderShipped", PostgresEventData.FromJson("{}"), null, CreatedAt);

        row.DecodedEvent.Payload.ShouldBe("OrderShipped:{}");
        first.Payload.ShouldBe("OrderPlaced:{}");
        decoder.DecodeCount.ShouldBe(2);
    }

    [Test]
    public void GetEvent_WhenItCannotBeDecoded_ThrowsTheSameExceptionToAllWhoAsk()
    {
        var decoder = new CountingDecoder { Failure = new InvalidOperationException("Cannot decode.") };
        var row = CreateRow(decoder);

        var first = Should.Throw<InvalidOperationException>(() => row.DecodedEvent);
        var second = Should.Throw<InvalidOperationException>(() => row.DecodedEvent);
        var third = Should.Throw<InvalidOperationException>(() => row.DecodedPayload);

        second.ShouldBeSameAs(first);
        third.ShouldBeSameAs(first);
        decoder.DecodeCount.ShouldBe(1);
    }

    [Test]
    public void Set_WhenTheLastRowCouldNotBeDecoded_ForgetsThat()
    {
        var decoder = new CountingDecoder { Failure = new InvalidOperationException("Cannot decode.") };
        var row = CreateRow(decoder);
        Should.Throw<InvalidOperationException>(() => row.DecodedEvent);

        decoder.Failure = null;
        row.Set(8, "order-2", 0, "OrderShipped", PostgresEventData.FromJson("{}"), null, CreatedAt);

        row.DecodedEvent.Payload.ShouldBe("OrderShipped:{}");
    }

    [Test]
    public void TryGetMetadata_WhenNotAsked_DecodesNothing()
    {
        var decoder = new CountingDecoder();
        var row = CreateRow(decoder, metadata: """{"tenant":"acme"}""");

        row.TryGetMetadata("tenant", out _).ShouldBeTrue();

        decoder.DecodeCount.ShouldBe(0);
    }

    [Test]
    public void GetEvent_WhenMetadataIsLeftOutOfEvents_StillLetsAFilterSeeIt()
    {
        var decoder = new CountingDecoder();
        var row = CreateRow(decoder, metadata: """{"tenant":"acme"}""", includeMetadata: false);

        row.TryGetMetadata("tenant", out var tenant).ShouldBeTrue();
        tenant.ShouldBe("acme");
        row.DecodedEvent.Context.Metadata.ShouldBeEmpty();
    }

    [TestCase("""{"tenant":"acme","user":"bob"}""", "tenant", "acme")]
    [TestCase("""{"tenant":"acme","user":"bob"}""", "user", "bob")]
    [TestCase("""{ "tenant" : "acme" }""", "tenant", "acme")]
    [TestCase("""{"tenant":""}""", "tenant", "")]
    [TestCase("""{"tenant":"o'brien \"and\" sons\\"}""", "tenant", """o'brien "and" sons\""")]
    [TestCase("""{"tenant":"café"}""", "tenant", "café")]
    [TestCase("""{"tenant":"acme"}""", "tenant", "acme")]
    [TestCase("""{"ключ":"значение"}""", "ключ", "значение")]
    [TestCase("""{"nested":{"tenant":"no"},"tenant":"acme"}""", "tenant", "acme")]
    [TestCase("""{"count":12}""", "count", "12")]
    public void TryGetMetadata_WhenTheKeyIsStored_GivesItsValue(string metadata, string key, string expected)
    {
        CreateRow(new CountingDecoder(), metadata).TryGetMetadata(key, out var value).ShouldBeTrue();

        value.ShouldBe(expected);
    }

    [TestCase(null, "tenant")]
    [TestCase("{}", "tenant")]
    [TestCase("""{"Tenant":"acme"}""", "tenant")]
    [TestCase("""{"nested":{"tenant":"acme"}}""", "tenant")]
    [TestCase("""["tenant"]""", "tenant")]
    public void TryGetMetadata_WhenTheKeyIsNotStored_GivesNothing(string? metadata, string key)
    {
        CreateRow(new CountingDecoder(), metadata).TryGetMetadata(key, out _).ShouldBeFalse();
    }

    private static EventLogRow<string> CreateRow(
        CountingDecoder decoder,
        string? metadata = null,
        bool includeMetadata = true)
    {
        var row = new EventLogRow<string>(decoder, includeMetadata);
        row.Set(7, "order-1", 3, "OrderPlaced", PostgresEventData.FromJson("{}"), metadata, CreatedAt);
        return row;
    }

    private sealed class CountingDecoder : IEventDecoder<string, PostgresEventData, string>
    {
        public int DecodeCount { get; private set; }

        public Exception? Failure { get; set; }

        public DecodedEvent<string> Decode(string eventName, PostgresEventData eventData, string? metadata)
        {
            DecodeCount++;

            if (Failure is not null)
                throw Failure;

            return DecodedEvent.Create(
                $"{eventName}:{eventData.Json}",
                metadata is null ? [] : new Dictionary<string, string> { ["raw"] = metadata });
        }

        public IReadOnlyCollection<string> ResolveEventNames(Type eventType) => [];
    }
}