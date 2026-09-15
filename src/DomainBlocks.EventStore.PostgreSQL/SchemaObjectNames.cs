using System.Text.RegularExpressions;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The fully qualified, quoted names of the database objects used by the event store, derived from a validated schema
/// name so that they are safe to interpolate into SQL.
/// </summary>
internal sealed partial class SchemaObjectNames
{
    public const string EventLogTableName = "event_log";
    private const string AppendEventsFunctionName = "append_events";
    private const string ExpectedStateKindTypeName = "expected_state_kind";

    public SchemaObjectNames(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (!SchemaNameRegex().IsMatch(schema))
        {
            throw new ArgumentException(
                $"Schema name '{schema}' is invalid. It must match {SchemaNameRegex()}.",
                nameof(schema));
        }

        Schema = schema;
        QuotedSchema = $"\"{schema}\"";
        EventLog = $"{QuotedSchema}.\"{EventLogTableName}\"";
        AppendEventsFunction = $"{QuotedSchema}.\"{AppendEventsFunctionName}\"";
        ExpectedStateKindType = $"{QuotedSchema}.\"{ExpectedStateKindTypeName}\"";
        Publication = $"{schema}_{EventLogTableName}_pub";
    }

    public string Schema { get; }

    public string QuotedSchema { get; }

    public string EventLog { get; }

    public string AppendEventsFunction { get; }

    public string ExpectedStateKindType { get; }

    /// <summary>
    /// The name of the logical replication publication. Publication names are database-wide, so the schema is embedded.
    /// </summary>
    public string Publication { get; }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SchemaNameRegex();
}