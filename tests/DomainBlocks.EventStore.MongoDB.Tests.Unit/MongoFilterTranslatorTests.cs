using DomainBlocks.EventStore.Filtering;
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
        ShouldTranslate(EventFilter.StreamIds("order-1"), """{ "streamId" : { "$in" : ["order-1"] } }""");
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
        var filter = EventFilter.EventNames("A") & (EventFilter.StreamIds("s") | !EventFilter.MetadataExists("k"));

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
        MongoFilterTranslator.CanPush(EventFilter.EventNames("A")).ShouldBeTrue();
        MongoFilterTranslator.CanPush(EventFilter.StreamIds("s")).ShouldBeTrue();
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
    }

    [Test]
    public void Translate_WhenGivenAFieldPrefix_PutsItBeforeEveryField()
    {
        var filter = EventFilter.EventNames("A") &
                     (EventFilter.StreamIdStartsWith("s") | !EventFilter.Metadata("k", "v")) &
                     EventFilter.CreatedBefore(Noon);

        var expected = BsonDocument.Parse(
            """
            { "$and" : [
                { "fullDocument.eventName" : { "$in" : ["A"] } },
                { "fullDocument.createdAtUtc" : { "$lt" : { "$date" : "2026-01-01T12:00:00Z" } } },
                { "$or" : [
                    { "fullDocument.streamId" : "a regular expression, put in below" },
                    { "$nor" : [{ "fullDocument.metadata.k" : { "$in" : ["v"] } }] }] }] }
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
        Render(EventFilter.EventNames("A")).ShouldBe(
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