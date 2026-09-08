namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Loads the embedded SQL scripts, substituting the schema token with a validated schema name.
/// </summary>
internal static class SqlScripts
{
    public const string SchemaToken = "__schema__";

    public static string Schema(SqlNames names) => Load("schema.sql", names);

    public static string AppendEvents(SqlNames names) => Load("append_events.sql", names);

    private static string Load(string fileName, SqlNames names)
    {
        var assembly = typeof(SqlScripts).Assembly;
        var resourceName = $"{typeof(SqlScripts).Namespace}.Sql.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName) ??
                           throw new InvalidOperationException($"Embedded SQL script '{resourceName}' not found.");

        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        // The schema name is validated to be a plain lower-case identifier, so it is safe to use unquoted.
        return sql.Replace(SchemaToken, names.Schema);
    }
}
