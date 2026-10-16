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
    public void TryGetMetadata_WhenInvoked_DoesNotDecodeEvent()
    {
        var decoder = new CountingDecoder();
        var document = new EventLogDocument<string>(decoder);
        document.Set(Stored("OrderPlaced", "order-1", new BsonDocument("tenant", "acme")));

        document.TryGetMetadata("tenant", out _);

        decoder.DecodeCount.ShouldBe(0);
    }

    [Test]
    public void Payload_FirstRead_DecodesPayloadAndMetadata()
    {
        var document = CreateDocument(new BsonDocument("tenant", "acme"));

        document.Payload.ShouldBe("OrderPlaced");
        document.Metadata.ShouldBe(new Dictionary<string, string> { ["tenant"] = "acme" });
    }

    [Test]
    public void Context_WhenRead_ReadsContextWithoutDecoding()
    {
        var decoder = new CountingDecoder();
        var document = new EventLogDocument<string>(decoder);
        document.Set(Stored("OrderPlaced", "order-1"));

        var context = document.Context;

        context.LogPosition.ShouldBe(LogPosition.FromInt64(7));
        context.StreamId.ShouldBe("order-1");
        context.StreamPosition.ShouldBe(StreamPosition.FromInt64(3));
        context.EventName.ShouldBe("OrderPlaced");
        context.CreatedAt.ShouldBe(new DateTimeOffset(CreatedAtUtc));
        decoder.DecodeCount.ShouldBe(0);
    }

    [Test]
    public void Context_AfterDocumentIsSetAgain_ReadsNewContext()
    {
        var document = new EventLogDocument<string>(new CountingDecoder());
        document.Set(Stored("OrderPlaced", "order-1"));
        _ = document.Context;

        document.Set(Stored("OrderShipped", "order-2"));

        document.Context.StreamId.ShouldBe("order-2");
        document.Context.EventName.ShouldBe("OrderShipped");
    }

    [Test]
    public void PayloadAndMetadata_ReadSeveralTimes_DecodeOnce()
    {
        var decoder = new CountingDecoder();
        var document = new EventLogDocument<string>(decoder);
        document.Set(Stored("OrderPlaced", "order-1"));

        var first = document.Payload;
        var metadata = document.Metadata;
        var second = document.Payload;

        second.ShouldBeSameAs(first);
        document.Metadata.ShouldBeSameAs(metadata);
        decoder.DecodeCount.ShouldBe(1);
    }

    [Test]
    public void Payload_NeverRead_NeverDecodes()
    {
        var decoder = new CountingDecoder();
        var document = new EventLogDocument<string>(decoder);

        document.Set(Stored("OrderPlaced", "order-1"));
        document.Set(Stored("OrderShipped", "order-1"));

        decoder.DecodeCount.ShouldBe(0);
    }

    [Test]
    public void Payload_AfterDocumentIsSetAgain_DecodesNewEvent()
    {
        var decoder = new CountingDecoder();
        var document = new EventLogDocument<string>(decoder);
        document.Set(Stored("OrderPlaced", "order-1"));
        document.Payload.ShouldBe("OrderPlaced");

        document.Set(Stored("OrderShipped", "order-1"));

        document.Payload.ShouldBe("OrderShipped");
        decoder.DecodeCount.ShouldBe(2);
    }

    [Test]
    public void PayloadAndMetadata_WhenDecodingFails_ThrowSameExceptionToEveryCallAndDecodeOnce()
    {
        var decoder = new CountingDecoder { Failure = new InvalidOperationException("Cannot decode.") };
        var document = new EventLogDocument<string>(decoder);
        document.Set(Stored("OrderPlaced", "order-1"));

        var first = Should.Throw<InvalidOperationException>(() => document.Payload);
        var second = Should.Throw<InvalidOperationException>(() => document.Metadata);

        second.ShouldBeSameAs(first);
        decoder.DecodeCount.ShouldBe(1);
    }

    [Test]
    public void Payload_AfterFailedDocumentIsSetAgain_DecodesNewEvent()
    {
        var decoder = new CountingDecoder { Failure = new InvalidOperationException("Cannot decode.") };
        var document = new EventLogDocument<string>(decoder);
        document.Set(Stored("OrderPlaced", "order-1"));
        Should.Throw<InvalidOperationException>(() => document.Payload);

        decoder.Failure = null;
        document.Set(Stored("OrderShipped", "order-1"));

        document.Payload.ShouldBe("OrderShipped");
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