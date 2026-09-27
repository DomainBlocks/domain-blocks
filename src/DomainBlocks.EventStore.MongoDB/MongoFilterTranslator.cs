using System.Text.RegularExpressions;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Translates event filters into queries over the documents of the event log.
/// </summary>
internal static class MongoFilterTranslator
{
    /// <summary>
    /// Whether <see cref="Translate"/> has a translation for the leaf. It is every leaf that is about a field, but for
    /// a name that a query would take for something else, which is left to be tested in process: a metadata key that
    /// reads as a path or an operator, or a payload name that reads as an operator or as an index into an array.
    /// </summary>
    public static bool CanPush(EventFilter leaf)
    {
        return leaf switch
        {
            EventNameFilter or StreamIdFilter or CreatedAtFilter => true,

            // A prefix goes into a regular expression, which the driver writes as text that a NUL would end.
            StreamIdPrefixFilter streamIdPrefix => !streamIdPrefix.Prefix.Contains('\0'),
            MetadataExistsFilter metadataExists => IsFieldName(metadataExists.Key),
            MetadataValueFilter metadataValue => IsFieldName(metadataValue.Key),
            PayloadValueFilter payloadValue => payloadValue.PathSegments.All(IsPayloadName),
            _ => false
        };
    }

    /// <param name="filter">A filter of leaves that <see cref="CanPush"/> accepts.</param>
    /// <param name="fieldPrefix">
    /// What comes before the name of each field, which for a change is <c>fullDocument.</c> and for the log nothing.
    /// </param>
    public static BsonDocument Translate(EventFilter filter, string fieldPrefix = "")
    {
        return filter switch
        {
            AllEventsFilter => [],

            // Nothing is not everything.
            NoEventsFilter => new BsonDocument("$nor", new BsonArray { new BsonDocument() }),

            AndFilter conjunction => new BsonDocument("$and", new BsonArray(conjunction.Operands.Select(Inner))),
            OrFilter disjunction => new BsonDocument("$or", new BsonArray(disjunction.Operands.Select(Inner))),

            // Unlike $not, this negates a whole query, and matches a document that lacks the field.
            NotFilter negation => new BsonDocument("$nor", new BsonArray { Inner(negation.Operand) }),

            EventNameFilter eventName => In(Field(EventLogEntry.FieldNames.EventName), eventName.Names),
            StreamIdFilter streamId => In(Field(EventLogEntry.FieldNames.StreamId), streamId.Ids),

            // Anchored, so that it can use the index, and escaped, so that the prefix is taken as it is.
            StreamIdPrefixFilter streamIdPrefix => new BsonDocument(
                Field(EventLogEntry.FieldNames.StreamId),
                new BsonDocument("$regex", new BsonRegularExpression($"^{Regex.Escape(streamIdPrefix.Prefix)}"))),

            MetadataExistsFilter metadataExists => new BsonDocument(
                Field(MetadataField(metadataExists.Key)),
                new BsonDocument("$exists", true)),

            MetadataValueFilter metadataValue => In(Field(MetadataField(metadataValue.Key)), metadataValue.Values),

            CreatedAtFilter createdAt => TranslateInterval(createdAt, Field(EventLogEntry.FieldNames.CreatedAtUtc)),

            // A payload that is not kept as a document has no fields, so nothing in it contradicts a predicate.
            // $ne matches a document that lacks the field, and of an array asks that no element is equal. A boolean
            // differs from one value by being the other, and any other value by coming before or after it.
            PayloadValueFilter { Comparison: PayloadComparison.NotEqual, Value: var value } differs =>
                value.Kind == PayloadValueKind.Boolean
                    ? Compare(differs.PathSegments, "$eq", !value.Boolean)
                    : new BsonDocument("$or", new BsonArray
                    {
                        Compare(differs.PathSegments, "$lt", ToBsonValue(value)),
                        Compare(differs.PathSegments, "$gt", ToBsonValue(value))
                    }),

            // A comparison only matches values of the kind it is given, and numbers of any width.
            PayloadValueFilter payloadValue => Compare(
                payloadValue.PathSegments,
                OperatorOf(payloadValue.Comparison),
                ToBsonValue(payloadValue.Value)),

            _ => throw new ArgumentException($"The filter '{filter}' cannot be translated to a query.", nameof(filter))
        };

        string Field(string name) => fieldPrefix + name;

        BsonDocument Compare(IEnumerable<string> segments, string comparison, BsonValue value) =>
            new(Field(PayloadField(segments)), new BsonDocument(comparison, value));

        BsonDocument Inner(EventFilter operand) => Translate(operand, fieldPrefix);
    }

    /// <summary>
    /// The pipeline of the store's change stream: the inserts that <paramref name="filter"/> selects, going by the
    /// document of each.
    /// </summary>
    public static PipelineDefinition<ChangeStreamDocument<BsonDocument>, ChangeStreamDocument<BsonDocument>>
        ToChangeStreamPipeline(EventFilter filter)
    {
        var inserts = Builders<ChangeStreamDocument<BsonDocument>>.Filter.Eq(
            x => x.OperationType,
            ChangeStreamOperationType.Insert);

        var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<BsonDocument>>().Match(inserts);

        return filter is AllEventsFilter
            ? pipeline
            : pipeline.Match((FilterDefinition<ChangeStreamDocument<BsonDocument>>)Translate(filter, "fullDocument."));
    }

    private static BsonDocument In(string field, IEnumerable<string> values) =>
        new(field, new BsonDocument("$in", new BsonArray(values)));

    private static BsonDocument TranslateInterval(CreatedAtFilter interval, string field)
    {
        var bounds = new BsonDocument();

        if (interval.From is { } from)
            bounds.Add("$gte", CeilingToMillisecond(from));

        if (interval.Before is { } before)
            bounds.Add("$lt", CeilingToMillisecond(before));

        return bounds.ElementCount == 0 ? [] : new BsonDocument(field, bounds);
    }

    // The field holds whole milliseconds, so no event is created between a bound and the next whole millisecond, and
    // both $gte and $lt select the same events with the bound rounded up as with the bound itself. The driver would
    // round the bound down.
    private static BsonDateTime CeilingToMillisecond(DateTimeOffset instant)
    {
        var excessTicks = instant.UtcTicks % TimeSpan.TicksPerMillisecond;
        var utc = instant.UtcDateTime;

        if (excessTicks == 0)
            return new BsonDateTime(utc);

        // The last instant there is, as a caller might say "with no end", has no later whole millisecond.
        var ticksToNext = TimeSpan.TicksPerMillisecond - excessTicks;

        return new BsonDateTime(
            utc.Ticks > DateTime.MaxValue.Ticks - ticksToNext ? utc.AddTicks(-excessTicks) : utc.AddTicks(ticksToNext));
    }

    private static string PayloadField(IEnumerable<string> segments) =>
        $"{EventLogEntry.FieldNames.EventData}.{string.Join('.', segments)}";

    private static string OperatorOf(PayloadComparison comparison)
    {
        return comparison switch
        {
            PayloadComparison.Equal => "$eq",
            PayloadComparison.GreaterThan => "$gt",
            PayloadComparison.GreaterThanOrEqual => "$gte",
            PayloadComparison.LessThan => "$lt",
            PayloadComparison.LessThanOrEqual => "$lte",
            _ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison, null)
        };
    }

    private static BsonValue ToBsonValue(PayloadValue value)
    {
        return value.Kind switch
        {
            PayloadValueKind.Text => value.Text!,
            PayloadValueKind.Number => new BsonDecimal128(value.Number),
            _ => value.Boolean
        };
    }

    private static string MetadataField(string key) => $"{EventLogEntry.FieldNames.Metadata}.{key}";

    // A dot makes a path of a name, and a leading dollar sign an operator.
    // Nor can the driver write a name with a NUL in it.
    private static bool IsFieldName(string key) =>
        key.Length > 0 && key[0] != '$' && !key.Contains('.') && !key.Contains('\0');

    // A name that is all digits is taken for an index into an array, if there is an array there.
    private static bool IsPayloadName(string name) => IsFieldName(name) && !name.All(char.IsAsciiDigit);
}