using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Tests.Shared;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

public class MongoFilterTranslatorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Translate_WhenAllOrNone_MatchesEverythingOrNothing()
    {
        ShouldTranslate(EventFilter.All, "{ }");
        ShouldTranslate(EventFilter.None, """{ "$nor" : [{ }] }""");
    }

    [Test]
    public void Translate_WhenByNameOrStream_ComparesWithTheValues()
    {
        ShouldTranslate(EventFilter.EventNames("B", "A"), """{ "eventName" : { "$in" : ["A", "B"] } }""");
        ShouldTranslate(EventFilter.StreamId("order-1"), """{ "streamId" : { "$in" : ["order-1"] } }""");
    }

    [Test]
    public void Translate_WhenByPrefix_AnchorsAndEscapesIt()
    {
        var expected = new BsonDocument(
            "streamId",
            new BsonDocument("$regex", new BsonRegularExpression(@"^order\.1\+")));

        MongoFilterTranslator.Translate(EventFilter.StreamIdStartsWith("order.1+")).ShouldBe(expected);
    }

    [Test]
    public void Translate_WhenByMetadata_LooksInTheStoredDocument()
    {
        ShouldTranslate(EventFilter.MetadataExists("tenant"), """{ "metadata.tenant" : { "$exists" : true } }""");
        ShouldTranslate(EventFilter.Metadata("tenant", "b", "a"), """{ "metadata.tenant" : { "$in" : ["a", "b"] } }""");
    }

    [Test]
    public void Translate_WhenByCreationTime_BoundsTheField()
    {
        ShouldTranslate(
            EventFilter.CreatedAtOrAfter(Noon),
            """{ "createdAtUtc" : { "$gte" : { "$date" : "2026-01-01T12:00:00Z" } } }""");

        ShouldTranslate(
            EventFilter.CreatedBefore(Noon),
            """{ "createdAtUtc" : { "$lt" : { "$date" : "2026-01-01T12:00:00Z" } } }""");
    }

    [Test]
    public void Translate_WhenABoundIsFinerThanAMillisecond_RoundsItUp()
    {
        // Between a millisecond and the next there is no event, so rounding up selects the same events.
        const string nextMillisecond = """{ "$date" : "2026-01-01T12:00:00.001Z" }""";

        ShouldTranslate(
            EventFilter.CreatedAtOrAfter(Noon.AddTicks(1)),
            $$"""{ "createdAtUtc" : { "$gte" : {{nextMillisecond}} } }""");

        ShouldTranslate(
            EventFilter.CreatedBefore(Noon.AddTicks(1)),
            $$"""{ "createdAtUtc" : { "$lt" : {{nextMillisecond}} } }""");

        ShouldTranslate(
            EventFilter.CreatedBefore(Noon.AddMilliseconds(1).ToOffset(TimeSpan.FromHours(5))),
            $$"""{ "createdAtUtc" : { "$lt" : {{nextMillisecond}} } }""");
    }

    [Test]
    public void Translate_WhenCombined_NestsTheOperands()
    {
        var filter = EventFilter.EventName("A") & (EventFilter.StreamId("s") | !EventFilter.MetadataExists("k"));

        ShouldTranslate(
            filter,
            """
            { "$and" : [
                { "eventName" : { "$in" : ["A"] } },
                { "$or" : [
                    { "streamId" : { "$in" : ["s"] } },
                    { "$nor" : [{ "metadata.k" : { "$exists" : true } }] }] }] }
            """);
    }

    [Test]
    public void Translate_WhenByStoredPayload_LooksInTheStoredDocument()
    {
        ShouldTranslate(
            StoredPayload.At("customer.name").EqualTo("Ann"),
            """{ "eventData.customer.name" : { "$eq" : "Ann" } }""");

        ShouldTranslate(StoredPayload.At("a").EqualTo("x"), """{ "eventData.a" : { "$eq" : "x" } }""");
        ShouldTranslate(StoredPayload.At("a").EqualTo(true), """{ "eventData.a" : { "$eq" : true } }""");
    }

    [Test]
    public void Translate_WhenAStoredValueIsToDiffer_AsksForOneBeforeOrAfterIt()
    {
        // $ne matches a document that lacks the field, and of an array asks that no element is equal. An ordering
        // matches values of its own kind, and of an array any element.
        ShouldTranslate(
            StoredPayload.At("a").NotEqualTo("x"),
            """{ "$or" : [{ "eventData.a" : { "$lt" : "x" } }, { "eventData.a" : { "$gt" : "x" } }] }""");

        const string number = """{ "$numberDecimal" : "1.5" }""";

        ShouldTranslate(
            StoredPayload.At("a").NotEqualTo(1.5m),
            $$"""
              { "$or" : [{ "eventData.a" : { "$lt" : {{number}} } }, { "eventData.a" : { "$gt" : {{number}} } }] }
              """);

        ShouldTranslate(StoredPayload.At("a").NotEqualTo(true), """{ "eventData.a" : { "$eq" : false } }""");
    }

    [Test]
    public void Translate_WhenComparingAStoredNumber_GivesItAsADecimal()
    {
        const string number = """{ "$numberDecimal" : "1.5" }""";

        ShouldTranslate(StoredPayload.At("a").EqualTo(1.5m), $$"""{ "eventData.a" : { "$eq" : {{number}} } }""");
        ShouldTranslate(StoredPayload.At("a").GreaterThan(1.5m), $$"""{ "eventData.a" : { "$gt" : {{number}} } }""");
        ShouldTranslate(StoredPayload.At("a").LessThan(1.5m), $$"""{ "eventData.a" : { "$lt" : {{number}} } }""");

        ShouldTranslate(
            StoredPayload.At("a").GreaterThanOrEqualTo(1.5m),
            $$"""{ "eventData.a" : { "$gte" : {{number}} } }""");

        ShouldTranslate(
            StoredPayload.At("a").LessThanOrEqualTo(1.5m),
            $$"""{ "eventData.a" : { "$lte" : {{number}} } }""");
    }

    [TestCase("customer.name", true)]
    [TestCase("lines.sku", true)]
    [TestCase("$where", false)]
    [TestCase("customer.$name", false)]
    [TestCase("lines.0", false)]
    [TestCase("lines.0.sku", false)]
    [TestCase("lines.a0", true)]
    public void CanPush_WhenByStoredPayload_GoesByWhetherAQueryWouldTakeTheNamesAsTheyAre(string path, bool expected)
    {
        MongoFilterTranslator.CanPush(StoredPayload.At(path).EqualTo(1)).ShouldBe(expected);
    }

    [TestCase("tenant", true)]
    [TestCase("tenant id", true)]
    [TestCase("a.b", false)]
    [TestCase("$where", false)]
    public void CanPush_WhenByMetadata_GoesByWhetherTheKeyIsAFieldName(string key, bool expected)
    {
        MongoFilterTranslator.CanPush(EventFilter.MetadataExists(key)).ShouldBe(expected);
        MongoFilterTranslator.CanPush(EventFilter.Metadata(key, "v")).ShouldBe(expected);
    }

    [Test]
    public void CanPush_WhenALeafIsAboutAField_IsTrue()
    {
        MongoFilterTranslator.CanPush(EventFilter.EventName("A")).ShouldBeTrue();
        MongoFilterTranslator.CanPush(EventFilter.StreamId("s")).ShouldBeTrue();
        MongoFilterTranslator.CanPush(EventFilter.StreamIdStartsWith("s")).ShouldBeTrue();
        MongoFilterTranslator.CanPush(EventFilter.CreatedBefore(Noon)).ShouldBeTrue();
    }

    [Test]
    public void CanPush_WhenALeafNeedsTheEvent_IsFalse()
    {
        MongoFilterTranslator.CanPush(EventFilter.OfType<string>()).ShouldBeFalse();
        MongoFilterTranslator.CanPush(EventFilter.OfType<string>(e => e.Length > 0)).ShouldBeFalse();
    }

    [Test]
    public void Translate_WhenGivenALeafItCannotPush_Throws()
    {
        Should.Throw<ArgumentException>(() => MongoFilterTranslator.Translate(EventFilter.OfType<string>()));
    }

    [Test]
    public void Translate_WhenABoundIsTheLastInstantThereIs_DoesNotRoundItUpPastIt()
    {
        // As a caller might say "with no end". There is no later whole millisecond to round up to.
        var translated = MongoFilterTranslator.Translate(EventFilter.CreatedBefore(DateTimeOffset.MaxValue));

        var lastMillisecond = new DateTimeOffset(9999, 12, 31, 23, 59, 59, 999, TimeSpan.Zero);

        translated["createdAtUtc"]["$lt"].AsBsonDateTime.MillisecondsSinceEpoch
            .ShouldBe(lastMillisecond.ToUnixTimeMilliseconds());
    }

    [TestCase("a\0b")]
    [TestCase("\0")]
    public void CanPush_WhenANameHasANul_IsFalse(string name)
    {
        // The driver cannot write such a name into a query at all.
        MongoFilterTranslator.CanPush(EventFilter.MetadataExists(name)).ShouldBeFalse();
        MongoFilterTranslator.CanPush(EventFilter.Metadata(name, "x")).ShouldBeFalse();
        MongoFilterTranslator.CanPush(EventFilter.StreamIdStartsWith(name)).ShouldBeFalse();
        MongoFilterTranslator.CanPush(StoredPayload.At(name).EqualTo(1)).ShouldBeFalse();
    }

    [Test]
    public void Translate_WhenGivenAFieldPrefix_PutsItBeforeEveryField()
    {
        var filter = EventFilter.EventName("A") &
                     (EventFilter.StreamIdStartsWith("s") | !EventFilter.Metadata("k", "v")) &
                     EventFilter.CreatedBefore(Noon) &
                     StoredPayload.At("a.b").EqualTo("x");

        var expected = BsonDocument.Parse(
            """
            { "$and" : [
                { "fullDocument.eventName" : { "$in" : ["A"] } },
                { "fullDocument.createdAtUtc" : { "$lt" : { "$date" : "2026-01-01T12:00:00Z" } } },
                { "$or" : [
                    { "fullDocument.streamId" : "a regular expression, put in below" },
                    { "$nor" : [{ "fullDocument.metadata.k" : { "$in" : ["v"] } }] }] },
                { "fullDocument.eventData.a.b" : { "$eq" : "x" } }] }
            """);

        // Text cannot say { $regex: /^s/ }: it is read as the expression alone.
        expected["$and"][2]["$or"][0]["fullDocument.streamId"] =
            new BsonDocument("$regex", new BsonRegularExpression("^s"));

        MongoFilterTranslator.Translate(filter, "fullDocument.").ShouldBe(expected);
    }

    [Test]
    public void ToChangeStreamPipeline_WhenThereIsNoFilter_SelectsInsertsAlone()
    {
        Render(EventFilter.All).ShouldBe([BsonDocument.Parse("""{ "$match" : { "operationType" : "insert" } }""")]);
    }

    [Test]
    public void ToChangeStreamPipeline_WhenThereIsAFilter_SelectsTheInsertsItSelects()
    {
        Render(EventFilter.EventName("A")).ShouldBe(
        [
            BsonDocument.Parse("""{ "$match" : { "operationType" : "insert" } }"""),
            BsonDocument.Parse("""{ "$match" : { "fullDocument.eventName" : { "$in" : ["A"] } } }""")
        ]);
    }

    private static List<BsonDocument> Render(EventFilter filter)
    {
        var input = new ChangeStreamDocumentSerializer<BsonDocument>(BsonDocumentSerializer.Instance);
        var arguments = new RenderArgs<ChangeStreamDocument<BsonDocument>>(input, BsonSerializer.SerializerRegistry);

        return [.. MongoFilterTranslator.ToChangeStreamPipeline(filter).Render(arguments).Documents];
    }

    private static void ShouldTranslate(EventFilter filter, string expectedJson)
    {
        MongoFilterTranslator.Translate(filter).ShouldBe(BsonDocument.Parse(expectedJson));
    }
}