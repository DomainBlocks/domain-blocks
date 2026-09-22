using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DomainBlocks.EventStore.Filtering;
using DomainBlocks.EventStore.Filtering.Nodes;

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
    private const string AppendStatusTypeName = "append_status";

    // PostgreSQL cuts a longer name short without a word, and the publications of two filters could then share one.
    private const int MaxIdentifierLength = 63;

    /// <param name="schema">The schema of the store.</param>
    /// <param name="subscriptionFilter">
    /// The subscription filter of the store, which its publication is named after, or <see langword="null"/> for none.
    /// </param>
    public SchemaObjectNames(string schema, EventFilter? subscriptionFilter = null)
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
        ExpectedStateKindType = $"{schema}.{ExpectedStateKindTypeName}";
        ExpectedStateKindArrayType = $"{ExpectedStateKindType}[]";
        AppendStatusType = $"{schema}.{AppendStatusTypeName}";
        PublicationPrefix = $"{schema}_{EventLogTableName}_pub";

        // Named after the filter, so that stores with different filters can share a schema. The text of a filter is the
        // same in every process, and the publication keeps it in full as its comment, in case two should hash alike.
        Publication = subscriptionFilter is null or AllEventsFilter
            ? PublicationPrefix
            : $"{PublicationPrefix}_{HashOf(subscriptionFilter.ToString())[..8]}";

        // Without a filter the name is as it always was, and a store that only appends and reads never uses it.
        if (Publication != PublicationPrefix && Publication.Length > MaxIdentifierLength)
        {
            throw new ArgumentException(
                $"The publication name '{Publication}' is longer than the {MaxIdentifierLength} characters that " +
                "PostgreSQL keeps of a name. Use a shorter schema name with a subscription filter.",
                nameof(schema));
        }
    }

    public string Schema { get; }

    public string QuotedSchema { get; }

    public string EventLog { get; }

    public string AppendEventsFunction { get; }

    /// <summary>
    /// The names of the append protocol enums, unquoted and schema-qualified, which is the form Npgsql's type
    /// mapping takes.
    /// </summary>
    public string ExpectedStateKindType { get; }

    public string ExpectedStateKindArrayType { get; }

    public string AppendStatusType { get; }

    /// <summary>
    /// The name of the logical replication publication. Publication names are database-wide, so the schema is embedded.
    /// </summary>
    public string Publication { get; }

    /// <summary>
    /// What the name of every publication of the schema starts with, whatever its filter.
    /// </summary>
    public string PublicationPrefix { get; }

    private static string HashOf(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SchemaNameRegex();
}