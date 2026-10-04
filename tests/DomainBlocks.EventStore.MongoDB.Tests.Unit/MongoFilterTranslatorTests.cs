using DomainBlocks.EventStore.Filtering;
using MongoDB.Bson;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.MongoDB.Tests.Unit;

public class MongoFilterTranslatorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly long NoonMilliseconds = Noon.ToUnixTimeMilliseconds();

    [Test]
    public void Translate_All_IsEmptyQuery()
    {
        MongoFilterTranslator.Translate(EventFilter.All).ShouldBe([]);
    }

    [Test]
    public void Translate_None_MatchesNoId()
    {
        MongoFilterTranslator.Translate(EventFilter.None).ShouldBe(BsonDocument.Parse("{ _id: { $in: [] } }"));
    }

    [Test]
    public void Translate_EventNames_MatchesNameInSet()
    {
        var query = MongoFilterTranslator.Translate(EventFilter.EventNames("OrderShipped", "OrderPlaced"));

        query.ShouldBe(BsonDocument.Parse("{ eventName: { $in: ['OrderPlaced', 'OrderShipped'] } }"));
    }

    [Test]
    public void Translate_StreamIds_MatchesStreamIdInSet()
    {
        var query = MongoFilterTranslator.Translate(EventFilter.StreamIds("order-2", "order-1"));

        query.ShouldBe(BsonDocument.Parse("{ streamId: { $in: ['order-1', 'order-2'] } }"));
    }

    [Test]
    public void Translate_StreamIdStartsWith_MatchesAnchoredRegularExpression()
    {
        var query = MongoFilterTranslator.Translate(EventFilter.StreamIdStartsWith("order-"));

        query.ShouldBe(new BsonDocument("streamId", new BsonDocument("$regex", new BsonRegularExpression("^order-"))));
    }

    [TestCase("invoice.1", @"^invoice\.1")]
    [TestCase("order%3", "^order%3")]
    [TestCase("a+b*(c)[d]", @"^a\+b\*\(c\)\[d]")]
    [TestCase(@"back\slash", @"^back\\slash")]
    [TestCase("^start$", @"^\^start\$")]
    public void Translate_StreamIdStartsWithSpecialCharacters_EscapesPrefix(string prefix, string expectedPattern)
    {
        var query = MongoFilterTranslator.Translate(EventFilter.StreamIdStartsWith(prefix));

        query["streamId"]["$regex"].AsBsonRegularExpression.Pattern.ShouldBe(expectedPattern);
        query["streamId"]["$regex"].AsBsonRegularExpression.Options.ShouldBeEmpty();
    }

    [Test]
    public void Translate_MetadataExists_ChecksFieldOfMetadataDocument()
    {
        var query = MongoFilterTranslator.Translate(EventFilter.MetadataExists("tenant"));

        query.ShouldBe(BsonDocument.Parse("{ 'metadata.tenant': { $exists: true } }"));
    }

    [Test]
    public void Translate_Metadata_MatchesValueInSet()
    {
        var query = MongoFilterTranslator.Translate(EventFilter.Metadata("tenant", "initech", "acme"));

        query.ShouldBe(BsonDocument.Parse("{ 'metadata.tenant': { $in: ['acme', 'initech'] } }"));
    }

    [TestCase("a.b")]
    [TestCase(".a")]
    [TestCase("$a")]
    public void Translate_MetadataKeyThatQueryCannotAddress_Throws(string key)
    {
        Should.Throw<EventFilterNotSupportedException>(() =>
            MongoFilterTranslator.Translate(EventFilter.MetadataExists(key)));

        Should.Throw<EventFilterNotSupportedException>(() =>
            MongoFilterTranslator.Translate(EventFilter.EventNames("OrderPlaced") & !EventFilter.Metadata(key, "v")));
    }

    [Test]
    public void Translate_MetadataKeyWithDollarSignAfterStart_IsAllowed()
    {
        var query = MongoFilterTranslator.Translate(EventFilter.MetadataExists("a$b"));

        query.ShouldBe(BsonDocument.Parse("{ 'metadata.a$b': { $exists: true } }"));
    }

    [Test]
    public void Translate_CreatedAtOrAfter_ComparesWithLowerBound()
    {
        var query = MongoFilterTranslator.Translate(EventFilter.CreatedAtOrAfter(Noon));

        query.ShouldBe(new BsonDocument("createdAtUtc", new BsonDocument("$gte", new BsonDateTime(NoonMilliseconds))));
    }

    [Test]
    public void Translate_CreatedBefore_ComparesWithUpperBound()
    {
        var query = MongoFilterTranslator.Translate(EventFilter.CreatedBefore(Noon));

        query.ShouldBe(new BsonDocument("createdAtUtc", new BsonDocument("$lt", new BsonDateTime(NoonMilliseconds))));
    }

    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(9_999, 1)]
    [TestCase(10_000, 1)]
    [TestCase(10_001, 2)]
    public void Translate_CreatedAtBoundBetweenMilliseconds_RoundsBoundUp(long ticks, long expectedMilliseconds)
    {
        var from = MongoFilterTranslator.Translate(EventFilter.CreatedAtOrAfter(Noon.AddTicks(ticks)));
        var before = MongoFilterTranslator.Translate(EventFilter.CreatedBefore(Noon.AddTicks(ticks)));

        var expected = NoonMilliseconds + expectedMilliseconds;
        from["createdAtUtc"]["$gte"].AsBsonDateTime.MillisecondsSinceEpoch.ShouldBe(expected);
        before["createdAtUtc"]["$lt"].AsBsonDateTime.MillisecondsSinceEpoch.ShouldBe(expected);
    }

    [Test]
    public void Translate_CreatedAtBoundWithOffset_ComparesInstants()
    {
        var bound = Noon.ToOffset(TimeSpan.FromHours(5));

        var query = MongoFilterTranslator.Translate(EventFilter.CreatedAtOrAfter(bound));

        query["createdAtUtc"]["$gte"].AsBsonDateTime.MillisecondsSinceEpoch.ShouldBe(NoonMilliseconds);
    }

    [Test]
    public void Translate_CreatedAtBoundAtLatestInstant_RoundsUpPastIt()
    {
        var query = MongoFilterTranslator.Translate(EventFilter.CreatedBefore(DateTimeOffset.MaxValue));

        var expected = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds() + 1;
        query["createdAtUtc"]["$lt"].AsBsonDateTime.MillisecondsSinceEpoch.ShouldBe(expected);
    }

    [Test]
    public void Translate_And_NestsOperandsInOrder()
    {
        var filter = EventFilter.EventNames("OrderPlaced") & EventFilter.Metadata("tenant", "acme");

        var query = MongoFilterTranslator.Translate(filter);

        query.ShouldBe(BsonDocument.Parse(
            "{ $and: [{ eventName: { $in: ['OrderPlaced'] } }, { 'metadata.tenant': { $in: ['acme'] } }] }"));
    }

    [Test]
    public void Translate_Or_NestsOperandsInOrder()
    {
        var filter = EventFilter.StreamIds("order-1") | EventFilter.MetadataExists("tenant");

        var query = MongoFilterTranslator.Translate(filter);

        query.ShouldBe(BsonDocument.Parse(
            "{ $or: [{ streamId: { $in: ['order-1'] } }, { 'metadata.tenant': { $exists: true } }] }"));
    }

    [Test]
    public void Translate_Not_NegatesWholeQueryOfOperand()
    {
        var query = MongoFilterTranslator.Translate(!EventFilter.MetadataExists("tenant"));

        query.ShouldBe(BsonDocument.Parse("{ $nor: [{ 'metadata.tenant': { $exists: true } }] }"));
    }

    [Test]
    public void Translate_NestedFilters_KeepsGrouping()
    {
        var filter =
            (EventFilter.EventNames("OrderPlaced") | EventFilter.StreamIds("invoice-1")) &
            !(EventFilter.MetadataExists("tenant") & EventFilter.EventNames("OrderShipped"));

        var query = MongoFilterTranslator.Translate(filter);

        query.ShouldBe(BsonDocument.Parse(
            """
            {
              $and: [
                { $or: [{ eventName: { $in: ['OrderPlaced'] } }, { streamId: { $in: ['invoice-1'] } }] },
                { $nor: [
                  { $and: [{ 'metadata.tenant': { $exists: true } }, { eventName: { $in: ['OrderShipped'] } }] }
                ] }
              ]
            }
            """));
    }
}