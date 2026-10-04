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
/// The text of a condition depends only on the shape of the filter. Its values are parameters, and a set of values is
/// one array parameter, so filters of the same shape share a statement.
/// </para>
/// <para>
/// Every condition is either true or false, as a filter either matches an event or does not. A condition over
/// metadata, which an event can lack, is closed with <c>IS TRUE</c>. Left as null, it would stay null under
/// <c>NOT</c>, and the negation of the filter would miss the events without metadata.
/// </para>
/// </remarks>
internal static class PostgresFilterTranslator
{
    /// <param name="filter">The filter to translate.</param>
    /// <param name="firstParameterIndex">
    /// The number of the condition's first parameter, which follows those that the query already has.
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

                // Unlike LIKE, this takes the prefix as it is, with nothing in it to escape.
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

    // The column holds whole microseconds, so no event is created between a bound and the next whole microsecond, and
    // both >= and < select the same events with the bound rounded up as with the bound itself. The bound cannot be
    // sent as it is, because it would be truncated to the microsecond below. The result is in UTC, which is the only
    // offset that a timestamptz parameter accepts.
    private static DateTimeOffset CeilingToMicrosecond(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        var excessTicks = utc.Ticks % TimeSpan.TicksPerMicrosecond;

        if (excessTicks == 0)
            return utc;

        var ticksToNext = TimeSpan.TicksPerMicrosecond - excessTicks;

        // The latest instant has no later whole microsecond, so it is rounded down instead. That differs only for an
        // event created in the last microsecond of the year 9999.
        return utc.Ticks > DateTimeOffset.MaxValue.Ticks - ticksToNext
            ? utc.AddTicks(-excessTicks)
            : utc.AddTicks(ticksToNext);
    }
}