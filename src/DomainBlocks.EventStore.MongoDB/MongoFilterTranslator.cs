using System.Diagnostics;
using System.Text.RegularExpressions;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Translates an <see cref="EventFilter"/> into a query over the documents of the event log.
/// </summary>
internal static class MongoFilterTranslator
{
    /// <exception cref="EventFilterNotSupportedException">
    /// The filter has a metadata key that contains a dot or starts with a dollar sign, which a query cannot address.
    /// </exception>
    public static BsonDocument Translate(EventFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return filter switch
        {
            AllEventsFilter => [],

            // An empty $in matches no document.
            NoEventsFilter => new BsonDocument(EventLogEntry.FieldNames.Position, In([])),

            EventNameFilter eventName => new BsonDocument(EventLogEntry.FieldNames.EventName, In(eventName.Names)),
            StreamIdFilter streamId => new BsonDocument(EventLogEntry.FieldNames.StreamId, In(streamId.Ids)),

            // The pattern is anchored so it can use the index on the stream ID, and escaped so the prefix is matched as
            // it is. A regular expression is case-sensitive unless it says otherwise.
            StreamIdPrefixFilter streamIdPrefix => new BsonDocument(
                EventLogEntry.FieldNames.StreamId,
                new BsonDocument("$regex", new BsonRegularExpression($"^{Regex.Escape(streamIdPrefix.Prefix)}"))),

            MetadataExistsFilter metadataExists => new BsonDocument(
                MetadataField(metadataExists.Key),
                new BsonDocument("$exists", true)),

            MetadataValueFilter metadataValue => new BsonDocument(
                MetadataField(metadataValue.Key),
                In(metadataValue.Values)),

            CreatedAtFilter createdAt => TranslateCreatedAt(createdAt),

            AndFilter and => new BsonDocument("$and", new BsonArray(and.Operands.Select(Translate))),
            OrFilter or => new BsonDocument("$or", new BsonArray(or.Operands.Select(Translate))),

            // Unlike $not, which negates one operator on one field, $nor negates a whole query. It also matches a
            // document that lacks the field, which the negation of a metadata filter needs.
            NotFilter not => new BsonDocument("$nor", new BsonArray { Translate(not.Operand) }),

            _ => throw new UnreachableException($"Unexpected filter of type '{filter.GetType()}'.")
        };
    }

    private static BsonDocument In(IEnumerable<string> values) => new("$in", new BsonArray(values));

    private static BsonDocument TranslateCreatedAt(CreatedAtFilter createdAt)
    {
        var bounds = new BsonDocument();

        if (createdAt.From is { } from)
            bounds.Add("$gte", CeilingToMillisecond(from));

        if (createdAt.Before is { } before)
            bounds.Add("$lt", CeilingToMillisecond(before));

        return bounds.ElementCount == 0 ? [] : new BsonDocument(EventLogEntry.FieldNames.CreatedAtUtc, bounds);
    }

    // The field holds whole milliseconds, so a bound rounded up to the next millisecond selects the same events with
    // $gte and $lt as the exact bound. Given as a DateTime, the bound would lose its extra ticks in the driver, which
    // rounds it down for instants after 1970 and up for earlier ones.
    private static BsonDateTime CeilingToMillisecond(DateTimeOffset instant)
    {
        var milliseconds = instant.ToUnixTimeMilliseconds();

        if (instant.UtcTicks % TimeSpan.TicksPerMillisecond != 0)
            milliseconds++;

        return new BsonDateTime(milliseconds);
    }

    // A dot in a field name makes it a path, and a leading dollar sign makes it an operator, so a key with either
    // cannot be addressed as the field it is stored in.
    private static string MetadataField(string key)
    {
        if (key.Contains('.') || key.StartsWith('$'))
        {
            throw new EventFilterNotSupportedException(
                $"MongoDB cannot filter on the metadata key '{key}', as a key that contains '.' or starts with '$' " +
                "cannot be addressed in a query.");
        }

        return $"{EventLogEntry.FieldNames.Metadata}.{key}";
    }
}