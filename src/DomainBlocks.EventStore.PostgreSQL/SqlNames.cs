using System.Text.RegularExpressions;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The fully qualified, quoted names of the database objects used by the event store, derived from a validated schema
/// name so that they are safe to interpolate into SQL.
/// </summary>
internal sealed partial class SqlNames
{
    public const string EventLogTableName = "event_log";
    public const string SequencesTableName = "sequences";
    public const string AppendEventsFunctionName = "append_events";
    public const string EventLogSequenceName = "event_log";

    public SqlNames(string schema)
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
        Sequences = $"{QuotedSchema}.\"{SequencesTableName}\"";
        AppendEventsFunction = $"{QuotedSchema}.\"{AppendEventsFunctionName}\"";
        PublicationName = $"{schema}_{EventLogTableName}_pub";
    }

    public string Schema { get; }

    public string QuotedSchema { get; }

    public string EventLog { get; }

    public string Sequences { get; }

    public string AppendEventsFunction { get; }

    /// <summary>
    /// The name of the logical replication publication. Publication names are database-wide, so the schema is embedded.
    /// </summary>
    public string PublicationName { get; }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SchemaNameRegex();
}
