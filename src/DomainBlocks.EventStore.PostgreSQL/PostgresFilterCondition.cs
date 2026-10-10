using System.Collections.Immutable;
using System.Diagnostics;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// A SQL condition over the event log columns, with its parameter values.
/// </summary>
/// <param name="sql">The condition, with parameters numbered from the index it was translated for.</param>
/// <param name="parameterValues">
/// The parameter values, in order: a <see cref="string"/>, a string array, or a <see cref="DateTimeOffset"/>.
/// </param>
internal sealed class PostgresFilterCondition(string sql, ImmutableArray<object> parameterValues)
{
    public string Sql { get; } = sql;

    public ImmutableArray<object> ParameterValues { get; } = parameterValues;

    /// <summary>
    /// Adds new parameters each time, as a parameter belongs to one command.
    /// </summary>
    public void AddParametersTo(NpgsqlParameterCollection parameters)
    {
        foreach (var value in ParameterValues)
        {
            parameters.Add(value switch
            {
                string text => new NpgsqlParameter<string> { TypedValue = text },
                string[] texts => new NpgsqlParameter<string[]> { TypedValue = texts },
                DateTimeOffset instant => new NpgsqlParameter<DateTimeOffset> { TypedValue = instant },
                _ => throw new UnreachableException($"Unexpected parameter value of type '{value.GetType()}'.")
            });
        }
    }
}