using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

public static class PostgresEventStoreAdmin
{
    /// <summary>
    /// Creates the schema, tables, sequence row, append function and publication used by the event store if they do
    /// not already exist. Safe to call on every start-up and from multiple processes concurrently.
    /// </summary>
    public static async Task EnsureInitializedAsync(
        NpgsqlDataSource dataSource,
        PostgresEventStoreOptions? options = null,
        PostgresEventStoreAdminOptions? adminOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgresEventStoreOptions();
        adminOptions ??= new PostgresEventStoreAdminOptions();

        var names = new SqlNames(options.Schema);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, SqlScripts.Schema(names), cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, SqlScripts.AppendEvents(names), cancellationToken).ConfigureAwait(false);

        if (adminOptions.CreatePublication)
            await EnsurePublicationAsync(connection, names, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Drops the publication and the schema, including all events. Intended for tests and tear-down tooling.
    /// </summary>
    public static async Task DropAsync(
        NpgsqlDataSource dataSource,
        PostgresEventStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgresEventStoreOptions();
        var names = new SqlNames(options.Schema);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
                connection,
                $"DROP PUBLICATION IF EXISTS {names.PublicationName}; DROP SCHEMA IF EXISTS {names.Schema} CASCADE;",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsurePublicationAsync(
        NpgsqlConnection connection,
        SqlNames names,
        CancellationToken cancellationToken)
    {
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_publication WHERE pubname = $1", connection);
        exists.Parameters.Add(new NpgsqlParameter<string> { TypedValue = names.PublicationName });

        if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null)
            return;

        await ExecuteAsync(
                connection,
                $"CREATE PUBLICATION {names.PublicationName} FOR TABLE {names.EventLog} WITH (publish = 'insert')",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
