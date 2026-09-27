using System.Globalization;
using System.Text;
using System.Text.Json;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// A condition over the columns of the event log, with the values of its parameters.
/// </summary>
/// <param name="Text">The condition. Its parameters are numbered from the index it was translated for.</param>
/// <param name="ParameterValues">
/// Each a <see cref="string"/>, a string array, a <see cref="DateTimeOffset"/>, a <see cref="PostgresJsonPath"/> or a
/// <see cref="PostgresJsonb"/>.
/// </param>
internal sealed record PostgresFilterSql(string Text, IReadOnlyList<object> ParameterValues)
{
    // A parameter belongs to one command, so they are made afresh for each.
    public void AddParametersTo(NpgsqlParameterCollection parameters)
    {
        foreach (var value in ParameterValues)
        {
            parameters.Add(value switch
            {
                string text => new NpgsqlParameter<string> { TypedValue = text },
                string[] texts => new NpgsqlParameter<string[]> { TypedValue = texts },
                DateTimeOffset instant => new NpgsqlParameter<DateTimeOffset> { TypedValue = instant },

                // Sent as what they are. Sent as text and cast in the statement, they are cast again for every row
                // once the server settles on a generic plan, which made a read four times slower.
                PostgresJsonPath path => new NpgsqlParameter { Value = path.Text, NpgsqlDbType = NpgsqlDbType.JsonPath },
                PostgresJsonb json => new NpgsqlParameter { Value = json.Text, NpgsqlDbType = NpgsqlDbType.Jsonb },
                _ => throw new InvalidOperationException($"Unexpected parameter value of type {value.GetType()}.")
            });
        }
    }
}

/// <summary>
/// A jsonpath, as a parameter of its own type.
/// </summary>
internal sealed record PostgresJsonPath(string Text);

/// <summary>
/// A jsonb value, as a parameter of its own type.
/// </summary>
internal sealed record PostgresJsonb(string Text);

/// <summary>
/// Translates event filters into SQL over the event log.
/// </summary>
/// <remarks>
/// The text depends on the shape of a filter and never on its values, which are parameters, a set of them being one
/// array. So filters of one shape share a statement. Every condition is either true or false, as a filter either
/// matches or does not: a condition over a column that can be null is closed with <c>IS TRUE</c>, or <c>NOT</c> would
/// leave it null where the negation of a filter matches.
/// </remarks>
internal static class PostgresFilterTranslator
{
    /// <summary>
    /// Whether <see cref="Translate"/> has a translation for the leaf. It is every leaf that is about a column. A
    /// payload that is kept as bytes is in another column, so nothing in it contradicts a predicate.
    /// </summary>
    public static bool CanPush(EventFilter leaf)
    {
        return leaf
            is EventNameFilter
            or StreamIdFilter
            or StreamIdPrefixFilter
            or MetadataExistsFilter
            or MetadataValueFilter
            or CreatedAtFilter
            or PayloadValueFilter;
    }

    /// <param name="filter">A filter of leaves that <see cref="CanPush"/> accepts.</param>
    /// <param name="firstParameterIndex">The number of its first parameter, after those the query already has.</param>
    public static PostgresFilterSql Translate(EventFilter filter, int firstParameterIndex) =>
        TranslateCore(filter, firstParameterIndex, asLiterals: false);

    /// <summary>
    /// Translates a filter into a condition with its values written into it, for a statement that takes no parameters,
    /// as the row filter of a publication does not.
    /// </summary>
    /// <param name="filter">A filter of leaves that <see cref="CanPush"/> accepts.</param>
    public static string TranslateToLiteralSql(EventFilter filter) => TranslateCore(filter, 1, asLiterals: true).Text;

    /// <summary>
    /// The row filter of a publication for the subscription filter of a store, or <see langword="null"/> for none.
    /// </summary>
    /// <exception cref="EventFilterNotSupportedException">
    /// The database cannot evaluate the whole of the filter as it stands.
    /// </exception>
    public static string? ToPublicationRowFilter(EventFilter subscriptionFilter)
    {
        if (subscriptionFilter is AllEventsFilter)
            return null;

        var plan = EventFilterPlan.Create(subscriptionFilter, FilterPushdownMode.Require, RefuseTypes, CanPush);

        return TranslateToLiteralSql(plan.Pushdown);

        // The names that a type is read as go by the codec of a store. A publication is made by whoever initializes
        // the schema, who has none, and is named after the filter alone, so it cannot go by what a codec says.
        static IReadOnlyCollection<string> RefuseTypes(Type eventType)
        {
            throw new EventFilterNotSupportedException(
                $"The subscription filter of a store cannot select by event type, and was given '{eventType}'. " +
                "Select by the names that the events are stored under instead.");
        }
    }

    private static PostgresFilterSql TranslateCore(EventFilter filter, int firstParameterIndex, bool asLiterals)
    {
        var text = new StringBuilder();
        var parameterValues = new List<object>();

        Append(filter);

        return new PostgresFilterSql(text.ToString(), parameterValues);

        void Append(EventFilter current)
        {
            switch (current)
            {
                case AllEventsFilter:
                    text.Append("TRUE");
                    break;

                case NoEventsFilter:
                    text.Append("FALSE");
                    break;

                case AndFilter conjunction:
                    AppendOperands(conjunction.Operands, " AND ");
                    break;

                case OrFilter disjunction:
                    AppendOperands(disjunction.Operands, " OR ");
                    break;

                case NotFilter negation:
                    text.Append("NOT ");
                    AppendOperands([negation.Operand], "");
                    break;

                case EventNameFilter eventName:
                    text.Append("event_name = ANY(").Append(Parameter(eventName.Names.ToArray())).Append(')');
                    break;

                case StreamIdFilter streamId:
                    text.Append("stream_id = ANY(").Append(Parameter(streamId.Ids.ToArray())).Append(')');
                    break;

                // Unlike LIKE, this takes the prefix as it is, with nothing in it to escape.
                case StreamIdPrefixFilter streamIdPrefix:
                    text.Append("starts_with(stream_id, ").Append(Parameter(streamIdPrefix.Prefix)).Append(')');
                    break;

                case MetadataExistsFilter metadataExists:
                    text.Append("(metadata ? ").Append(Parameter(metadataExists.Key)).Append(") IS TRUE");
                    break;

                case MetadataValueFilter metadataValue:
                    text.Append("(metadata ->> ").Append(Parameter(metadataValue.Key));
                    text.Append(" = ANY(").Append(Parameter(metadataValue.Values.ToArray())).Append(")) IS TRUE");
                    break;

                case CreatedAtFilter createdAt:
                    AppendInterval(createdAt);
                    break;

                // jsonpath is lax unless told otherwise, which is what a path means: an array stands for its elements,
                // and a value of another kind is no match and no error. The path and the value are parameters too.
                case PayloadValueFilter payloadValue:
                    var path = $"{ToJsonPath(payloadValue.PathSegments)} ? ({ToJsonPathCondition(payloadValue)})";

                    text.Append("jsonb_path_exists(event_data, ").Append(Parameter(new PostgresJsonPath(path)));
                    text.Append(", ").Append(Parameter(new PostgresJsonb(ToJsonPathVariables(payloadValue.Value))));
                    text.Append(") IS TRUE");
                    break;

                default:
                    throw new ArgumentException($"The filter '{current}' cannot be translated to SQL.", nameof(filter));
            }
        }

        void AppendOperands(IReadOnlyList<EventFilter> operands, string separator)
        {
            text.Append('(');

            for (var i = 0; i < operands.Count; i++)
            {
                if (i > 0)
                    text.Append(separator);

                Append(operands[i]);
            }

            text.Append(')');
        }

        void AppendInterval(CreatedAtFilter interval)
        {
            text.Append('(');

            if (interval.From is { } from)
                text.Append("created_at >= ").Append(Parameter(CeilingToMicrosecond(from)));

            if (interval is { From: not null, Before: not null })
                text.Append(" AND ");

            if (interval.Before is { } before)
                text.Append("created_at < ").Append(Parameter(CeilingToMicrosecond(before)));

            if (interval is { From: null, Before: null })
                text.Append("TRUE");

            text.Append(')');
        }

        string Parameter(object value)
        {
            if (asLiterals)
                return ToLiteral(value);

            parameterValues.Add(value);

            return string.Create(CultureInfo.InvariantCulture, $"${firstParameterIndex + parameterValues.Count - 1}");
        }
    }

    private static string ToLiteral(object value)
    {
        return value switch
        {
            string text => QuoteLiteral(text),
            string[] texts => $"ARRAY[{string.Join(", ", texts.Select(QuoteLiteral))}]::text[]",

            // In UTC, to the microsecond, so that it does not go by the time zone of whichever session reads it.
            DateTimeOffset instant =>
                QuoteLiteral(
                    instant.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff+00", CultureInfo.InvariantCulture)) +
                "::timestamptz",

            _ => throw new InvalidOperationException($"Unexpected value of type {value.GetType()}.")
        };
    }

    /// <summary>
    /// Writes text as a string constant that every server reads alike. A quote is doubled. What a backslash means in
    /// a plain string goes by <c>standard_conforming_strings</c>: where that is off it escapes, and <c>\'</c> would
    /// end the string early. So text with a backslash is written as an escape string, which means the same whatever
    /// the setting. A NUL cannot be in a string at all.
    /// </summary>
    public static string QuoteLiteral(string text)
    {
        if (text.Contains('\0'))
            throw new ArgumentException("A value of a filter cannot contain a NUL character.");

        var quoted = text.Replace("'", "''");

        return quoted.Contains('\\') ? $"E'{quoted.Replace("\\", "\\\\")}'" : $"'{quoted}'";
    }

    // Names are quoted as JSON strings, which is how jsonpath quotes them, so nothing in a name is taken for syntax.
    private static string ToJsonPath(IEnumerable<string> segments) =>
        "$" + string.Concat(segments.Select(x => $".{JsonSerializer.Serialize(x)}"));

    private static string ToJsonPathVariables(PayloadValue value)
    {
        object variable = value.Kind switch
        {
            PayloadValueKind.Text => value.Text!,
            PayloadValueKind.Number => value.Number,
            _ => value.Boolean
        };

        return JsonSerializer.Serialize(new Dictionary<string, object> { ["v"] = variable });
    }

    private static string ToJsonPathCondition(PayloadValueFilter filter)
    {
        if (filter.Comparison != PayloadComparison.NotEqual)
            return $"@ {OperatorOf(filter.Comparison)} $v";

        // To jsonpath a null differs from every value. Here it compares with none, as nothing of another kind does.
        var kind = filter.Value.Kind switch
        {
            PayloadValueKind.Text => "string",
            PayloadValueKind.Number => "number",
            _ => "boolean"
        };

        return $"@ != $v && @.type() == \"{kind}\"";
    }

    private static string OperatorOf(PayloadComparison comparison)
    {
        return comparison switch
        {
            PayloadComparison.Equal => "==",
            PayloadComparison.GreaterThan => ">",
            PayloadComparison.GreaterThanOrEqual => ">=",
            PayloadComparison.LessThan => "<",
            PayloadComparison.LessThanOrEqual => "<=",
            _ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison, null)
        };
    }

    // The column holds whole microseconds, so no event is created between a bound and the next whole microsecond, and
    // both >= and < select the same events with the bound rounded up as with the bound itself. The bound itself cannot
    // be given to the server, which would round it to the nearest.
    private static DateTimeOffset CeilingToMicrosecond(DateTimeOffset instant)
    {
        var excessTicks = instant.UtcTicks % TimeSpan.TicksPerMicrosecond;
        var utc = instant.ToUniversalTime();

        if (excessTicks == 0)
            return utc;

        // The last instant there is, as a caller might say "with no end", has no later whole microsecond.
        var ticksToNext = TimeSpan.TicksPerMicrosecond - excessTicks;

        return utc.UtcTicks > DateTimeOffset.MaxValue.UtcTicks - ticksToNext
            ? utc.AddTicks(-excessTicks)
            : utc.AddTicks(ticksToNext);
    }
}