using System.Collections.Immutable;
using System.Diagnostics;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// A condition over the columns of the event log, with the values of its parameters.
/// </summary>
/// <param name="sql">The condition. Its parameters are numbered from the index it was translated for.</param>
/// <param name="parameterValues">
/// The value of each parameter in order: a <see cref="string"/>, a string array, or a <see cref="DateTimeOffset"/>.
/// </param>
internal sealed class PostgresFilterCondition(string sql, ImmutableArray<object> parameterValues)
{
    public string Sql { get; } = sql;

    public ImmutableArray<object> ParameterValues { get; } = parameterValues;

    /// <summary>
    /// Adds the parameters of the condition to a command. A parameter belongs to one command, so new ones are created
    /// each time.
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