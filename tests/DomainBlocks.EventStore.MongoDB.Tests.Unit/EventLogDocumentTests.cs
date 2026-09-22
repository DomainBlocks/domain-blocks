using DomainBlocks.EventStore.Codecs;
using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

public class EventLogDocumentTests
{
    private static readonly DateTime CreatedAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Subject_WhenSet_ReadsTheFieldsOfTheDocument()
    {
        var document = new EventLogDocument<string>(new CountingDecoder(), includeMetadata: true);

        document.Set(Stored("OrderPlaced", "order-1"));

        document.EventName.ShouldBe("OrderPlaced");
        document.StreamId.ShouldBe("order-1");
        document.CreatedAt.ShouldBe(new DateTimeOffset(CreatedAt));
    }

    [Test]
    public void GetEvent_WhenAskedTwice_DecodesOnce()
    {
        var decoder = new CountingDecoder();
        var document = new EventLogDocument<string>(decoder, includeMetadata: true);
        document.Set(Stored("OrderPlaced", "order-1"));

        var first = document.DecodedEvent;

        document.DecodedEvent.Payload.ShouldBeSameAs(first.Payload);
        document.DecodedPayload.ShouldBeSameAs(first.Payload);
        decoder.DecodeCount.ShouldBe(1);
    }

    [Test]
    public void Set_WhenGivenTheNextChange_ForgetsTheEventOfTheLast()
    {
        var decoder = new CountingDecoder();
        var document = new EventLogDocument<string>(decoder, includeMetadata: true);

        document.Set(Stored("OrderPlaced", "order-1"));
        document.DecodedEvent.Payload.ShouldBe("OrderPlaced");

        document.Set(Stored("OrderShipped", "order-1"));

        document.DecodedEvent.Payload.ShouldBe("OrderShipped");
        decoder.DecodeCount.ShouldBe(2);
    }

    [Test]
    public void GetEvent_WhenItCannotBeDecoded_ThrowsTheSameExceptionToAllWhoAsk()
    {
        var decoder = new CountingDecoder { Failure = new InvalidOperationException("Cannot decode.") };
        var document = new EventLogDocument<string>(decoder, includeMetadata: true);
        var change = Stored("OrderPlaced", "order-1");

        document.Set(change);
        var first = Should.Throw<InvalidOperationException>(() => document.DecodedEvent);
        var second = Should.Throw<InvalidOperationException>(() => document.DecodedEvent);

        second.ShouldBeSameAs(first);
        decoder.DecodeCount.ShouldBe(1);

        decoder.Failure = null;
        document.Set(Stored("OrderShipped", "order-1"));
        document.DecodedEvent.Payload.ShouldBe("OrderShipped");
    }

    [Test]
    public void TryGetMetadata_WhenStoredAsADocument_LooksTheKeyUpWithoutDecoding()
    {
        var decoder = new CountingDecoder();
        var document = new EventLogDocument<string>(decoder, includeMetadata: true);
        document.Set(Stored("OrderPlaced", "order-1", new BsonDocument { { "tenant", "acme" }, { "count", 12 } }));

        document.TryGetMetadata("tenant", out var tenant).ShouldBeTrue();
        tenant.ShouldBe("acme");

        document.TryGetMetadata("count", out var count).ShouldBeTrue();
        count.ShouldBe("12");

        document.TryGetMetadata("Tenant", out _).ShouldBeFalse();
        decoder.DecodeCount.ShouldBe(0);
    }

    [Test]
    public void TryGetMetadata_WhenThereIsNoneOrItIsNotADocument_GivesNothing()
    {
        var document = new EventLogDocument<string>(new CountingDecoder(), includeMetadata: true);

        document.Set(Stored("OrderPlaced", "order-1"));
        document.TryGetMetadata("tenant", out _).ShouldBeFalse();

        document.Set(Stored("OrderPlaced", "order-1", BsonNull.Value));
        document.TryGetMetadata("tenant", out _).ShouldBeFalse();

        document.Set(Stored("OrderPlaced", "order-1", new BsonString("""{"tenant":"acme"}""")));
        document.TryGetMetadata("tenant", out _).ShouldBeFalse();
    }

    [Test]
    public void GetEvent_WhenMetadataIsLeftOutOfEvents_StillLetsAFilterSeeIt()
    {
        var document = new EventLogDocument<string>(new CountingDecoder(), includeMetadata: false);
        document.Set(Stored("OrderPlaced", "order-1", new BsonDocument("tenant", "acme")));

        document.TryGetMetadata("tenant", out _).ShouldBeTrue();
        document.DecodedEvent.Context.Metadata.ShouldBeEmpty();
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
            { EventLogEntry.FieldNames.CreatedAtUtc, CreatedAt }
        };

        if (metadata is not null)
            document.Add(EventLogEntry.FieldNames.Metadata, metadata);

        return document;
    }

    private sealed class CountingDecoder : IEventDecoder<string, BsonValue, BsonValue>
    {
        public int DecodeCount { get; private set; }

        public Exception? Failure { get; set; }

        public DecodedEvent<string> Decode(string eventName, BsonValue eventData, BsonValue? metadata)
        {
            DecodeCount++;

            if (Failure is not null)
                throw Failure;

            var decoded = metadata is BsonDocument entries
                ? entries.ToDictionary(x => x.Name, x => x.Value.ToString()!)
                : [];

            return DecodedEvent.Create(eventName, decoded);
        }

        public IReadOnlyCollection<string> ResolveEventNames(Type eventType) => [];
    }
}