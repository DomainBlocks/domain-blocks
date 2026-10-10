using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

public class EventLogDocumentTests
{
    private static readonly DateTime CreatedAtUtc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Set_Document_ExposesStoredValuesToFilter()
    {
        var document = new EventLogDocument<string>(new CountingDecoder());

        document.Set(Stored("OrderPlaced", "order-1"));

        document.EventName.ShouldBe("OrderPlaced");
        document.StreamId.ShouldBe("order-1");
        document.CreatedAt.ShouldBe(new DateTimeOffset(CreatedAtUtc));
    }

    [TestCase("tenant", "acme")]
    [TestCase("region", "eu")]
    [TestCase("note", "")]
    [TestCase("awkward", "o'brien \"and\" sons\\")]
    [TestCase("accented", "café")]
    public void TryGetMetadata_KeyInStoredMetadata_GetsItsValue(string key, string expected)
    {
        var metadata = new BsonDocument
        {
            { "tenant", "acme" },
            { "region", "eu" },
            { "note", "" },
            { "awkward", "o'brien \"and\" sons\\" },
            { "accented", "café" }
        };

        var document = CreateDocument(metadata);

        document.TryGetMetadata(key, out var value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [TestCase("Tenant")]
    [TestCase("tenan")]
    [TestCase("acme")]
    [TestCase("inner")]
    public void TryGetMetadata_KeyNotInStoredMetadata_GetsNothing(string key)
    {
        var metadata = new BsonDocument { { "tenant", "acme" }, { "outer", new BsonDocument("inner", "x") } };
        var document = CreateDocument(metadata);

        document.TryGetMetadata(key, out var value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Test]
    public void TryGetMetadata_MetadataAbsentNullOrNotADocument_GetsNothing()
    {
        var document = new EventLogDocument<string>(new CountingDecoder());

        document.Set(Stored("OrderPlaced", "order-1"));
        document.TryGetMetadata("tenant", out _).ShouldBeFalse();

        document.Set(Stored("OrderPlaced", "order-1", BsonNull.Value));
        document.TryGetMetadata("tenant", out _).ShouldBeFalse();

        document.Set(Stored("OrderPlaced", "order-1", new BsonString("""{"tenant":"acme"}""")));
        document.TryGetMetadata("tenant", out _).ShouldBeFalse();
    }

    [Test]
    public void TryGetMetadata_ValueThatIsNotAString_GetsItAsWritten()
    {
        var metadata = new BsonDocument { { "count", 12 }, { "flag", true }, { "nested", new BsonDocument("a", 1) } };
        var document = CreateDocument(metadata);

        document.TryGetMetadata("count", out var count).ShouldBeTrue();
        count.ShouldBe("12");

        document.TryGetMetadata("flag", out var flag).ShouldBeTrue();
        flag.ShouldBe("true");

        document.TryGetMetadata("nested", out var nested).ShouldBeTrue();
        nested.ShouldBe("{ \"a\" : 1 }");
    }

    [Test]
    public void TryGetMetadata_Document_DoesNotDecodeEvent()
    {
        var decoder = new CountingDecoder();
        var document = new EventLogDocument<string>(decoder);
        document.Set(Stored("OrderPlaced", "order-1", new BsonDocument("tenant", "acme")));

        document.TryGetMetadata("tenant", out _);

        decoder.DecodeCount.ShouldBe(0);
    }

    [Test]
    public void DecodedEvent_FirstRequest_DecodesEventWithItsContext()
    {
        var document = new EventLogDocument<string>(new CountingDecoder());
        document.Set(Stored("OrderPlaced", "order-1", new BsonDocument("tenant", "acme")));

        var e = document.DecodedEvent;

        e.Payload.ShouldBe("OrderPlaced");
        e.Context.LogPosition.ShouldBe(LogPosition.FromInt64(7));
        e.Context.StreamId.ShouldBe("order-1");
        e.Context.StreamPosition.ShouldBe(StreamPosition.FromInt64(3));
        e.Context.EventName.ShouldBe("OrderPlaced");
        e.Context.CreatedAt.ShouldBe(new DateTimeOffset(CreatedAtUtc));
        e.Context.Metadata.ShouldBe(new Dictionary<string, string> { ["tenant"] = "acme" });
    }

    [Test]
    public void DecodedEvent_RequestedSeveralTimes_DecodesOnce()
    {
        var decoder = new CountingDecoder();
        var document = new EventLogDocument<string>(decoder);
        document.Set(Stored("OrderPlaced", "order-1"));

        var first = document.DecodedEvent;
        var second = document.DecodedEvent;

        second.Payload.ShouldBeSameAs(first.Payload);
        decoder.DecodeCount.ShouldBe(1);
    }

    [Test]
    public void DecodedEvent_NeverRequested_NeverDecodes()
    {
        var decoder = new CountingDecoder();
        var document = new EventLogDocument<string>(decoder);

        document.Set(Stored("OrderPlaced", "order-1"));
        document.Set(Stored("OrderShipped", "order-1"));

        decoder.DecodeCount.ShouldBe(0);
    }

    [Test]
    public void DecodedEvent_AfterDocumentIsSetAgain_DecodesNewEvent()
    {
        var decoder = new CountingDecoder();
        var document = new EventLogDocument<string>(decoder);
        document.Set(Stored("OrderPlaced", "order-1"));
        document.DecodedEvent.Payload.ShouldBe("OrderPlaced");

        document.Set(Stored("OrderShipped", "order-1"));

        document.DecodedEvent.Payload.ShouldBe("OrderShipped");
        decoder.DecodeCount.ShouldBe(2);
    }

    [Test]
    public void DecodedEvent_WhenDecodingFails_ThrowsSameExceptionToEveryoneAndDecodesOnce()
    {
        var decoder = new CountingDecoder { Failure = new InvalidOperationException("Cannot decode.") };
        var document = new EventLogDocument<string>(decoder);
        document.Set(Stored("OrderPlaced", "order-1"));

        var first = Should.Throw<InvalidOperationException>(() => document.DecodedEvent);
        var second = Should.Throw<InvalidOperationException>(() => document.DecodedEvent);

        second.ShouldBeSameAs(first);
        decoder.DecodeCount.ShouldBe(1);
    }

    [Test]
    public void DecodedEvent_AfterFailedDocumentIsSetAgain_DecodesNewEvent()
    {
        var decoder = new CountingDecoder { Failure = new InvalidOperationException("Cannot decode.") };
        var document = new EventLogDocument<string>(decoder);
        document.Set(Stored("OrderPlaced", "order-1"));
        Should.Throw<InvalidOperationException>(() => document.DecodedEvent);

        decoder.Failure = null;
        document.Set(Stored("OrderShipped", "order-1"));

        document.DecodedEvent.Payload.ShouldBe("OrderShipped");
        decoder.DecodeCount.ShouldBe(2);
    }

    private static EventLogDocument<string> CreateDocument(BsonValue metadata)
    {
        var document = new EventLogDocument<string>(new CountingDecoder());
        document.Set(Stored("OrderPlaced", "order-1", metadata));

        return document;
    }

    private static BsonDocument Stored(string eventName, string streamId, BsonValue? metadata = null)
    {
        var document = new BsonDocument
        {
            { EventLogEntry.FieldNames.Position, 7L },
            { EventLogEntry.FieldNames.StreamId, streamId },
            { EventLogEntry.FieldNames.StreamPosition, 3L },
            { EventLogEntry.FieldNames.EventName, eventName },
            { EventLogEntry.FieldNames.EventData, new BsonDocument() },
            { EventLogEntry.FieldNames.CreatedAtUtc, CreatedAtUtc }
        };

        if (metadata is not null)
            document.Add(EventLogEntry.FieldNames.Metadata, metadata);

        return document;
    }
}