using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Translates an <see cref="EventFilter"/> into a condition over the columns of the event log.
/// </summary>
/// <remarks>
/// <para>
/// The text of a condition depends only on the shape of the filter. Values are passed as parameters, and a set of
/// values is passed as one array parameter, so filters of the same shape share a statement.
/// </para>
/// <para>
/// A metadata condition ends with <c>IS TRUE</c> because an event can lack the key. A null result would stay null under
/// <c>NOT</c>, and a negated filter would then miss the events without that metadata.
/// </para>
/// </remarks>
internal static class PostgresFilterTranslator
{
    /// <param name="filter">The filter to translate.</param>
    /// <param name="firstParameterIndex">
    /// The number of the condition's first parameter, after the query's own.
    /// </param>
    public static PostgresFilterCondition Translate(EventFilter filter, int firstParameterIndex)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(firstParameterIndex, 1);

        var sql = new StringBuilder();
        var parameterValues = ImmutableArray.CreateBuilder<object>();

        Append(filter);

        return new PostgresFilterCondition(sql.ToString(), parameterValues.ToImmutable());

        void Append(EventFilter node)
        {
            switch (node)
            {
                case AllEventsFilter:
                    sql.Append("TRUE");
                    break;

                case NoEventsFilter:
                    sql.Append("FALSE");
                    break;

                case EventNameFilter eventName:
                    sql.Append("event_name = ANY(").Append(Parameter(eventName.Names.ToArray())).Append(')');
                    break;

                case StreamIdFilter streamId:
                    sql.Append("stream_id = ANY(").Append(Parameter(streamId.Ids.ToArray())).Append(')');
                    break;

                // Unlike LIKE, starts_with takes the prefix as it is, so nothing in it needs escaping.
                case StreamIdPrefixFilter streamIdPrefix:
                    sql.Append("starts_with(stream_id, ").Append(Parameter(streamIdPrefix.Prefix)).Append(')');
                    break;

                case MetadataExistsFilter metadataExists:
                    sql.Append("(metadata ? ").Append(Parameter(metadataExists.Key)).Append(") IS TRUE");
                    break;

                case MetadataValueFilter metadataValue:
                    sql.Append("(metadata ->> ").Append(Parameter(metadataValue.Key));
                    sql.Append(" = ANY(").Append(Parameter(metadataValue.Values.ToArray())).Append(")) IS TRUE");
                    break;

                case CreatedAtFilter createdAt:
                    AppendCreatedAt(createdAt);
                    break;

                case AndFilter and:
                    AppendOperands(and.Operands, " AND ");
                    break;

                case OrFilter or:
                    AppendOperands(or.Operands, " OR ");
                    break;

                case NotFilter not:
                    sql.Append("(NOT ");
                    Append(not.Operand);
                    sql.Append(')');
                    break;

                default:
                    throw new UnreachableException($"Unexpected filter of type '{node.GetType()}'.");
            }
        }

        void AppendOperands(ImmutableArray<EventFilter> operands, string separator)
        {
            sql.Append('(');

            for (var i = 0; i < operands.Length; i++)
            {
                if (i > 0)
                    sql.Append(separator);

                Append(operands[i]);
            }

            sql.Append(')');
        }

        void AppendCreatedAt(CreatedAtFilter createdAt)
        {
            switch (createdAt)
            {
                case { From: { } from, Before: { } before }:
                    sql.Append("(created_at >= ").Append(Parameter(CeilingToMicrosecond(from)));
                    sql.Append(" AND created_at < ").Append(Parameter(CeilingToMicrosecond(before))).Append(')');
                    break;

                case { From: { } from }:
                    sql.Append("created_at >= ").Append(Parameter(CeilingToMicrosecond(from)));
                    break;

                case { Before: { } before }:
                    sql.Append("created_at < ").Append(Parameter(CeilingToMicrosecond(before)));
                    break;

                default:
                    sql.Append("TRUE");
                    break;
            }
        }

        string Parameter(object value)
        {
            parameterValues.Add(value);

            return string.Create(CultureInfo.InvariantCulture, $"${firstParameterIndex + parameterValues.Count - 1}");
        }
    }

    // The column holds whole microseconds, so a bound rounded up to the next microsecond selects the same events with
    // >= and < as the exact bound. Sent unrounded, the bound would lose its extra ticks in Npgsql, which rounds it down
    // for instants after 2000 and up for earlier ones. The result is UTC, the only offset Npgsql accepts for a
    // timestamptz parameter.
    private static DateTimeOffset CeilingToMicrosecond(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        var excessTicks = utc.Ticks % TimeSpan.TicksPerMicrosecond;

        if (excessTicks == 0)
            return utc;

        var ticksToNext = TimeSpan.TicksPerMicrosecond - excessTicks;

        // The latest instant has no later whole microsecond, so it is rounded down instead. That only differs for an
        // event created in the last microsecond of the year 9999.
        return utc.Ticks > DateTimeOffset.MaxValue.Ticks - ticksToNext
            ? utc.AddTicks(-excessTicks)
            : utc.AddTicks(ticksToNext);
    }
}