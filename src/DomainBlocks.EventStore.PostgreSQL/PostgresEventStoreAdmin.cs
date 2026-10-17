using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

public static class PostgresEventStoreAdmin
{
    /// <summary>
    /// Creates the schema, types, tables, and functions the event store uses, if they do not exist. Idempotent and safe
    /// to call concurrently from several processes.
    /// </summary>
    public static async Task EnsureInitializedAsync(
        NpgsqlDataSource dataSource,
        PostgresEventStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgresEventStoreOptions();

        var names = new SchemaObjectNames(options.Schema);

        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            await ExecuteAsync(connection, SqlScripts.Schema(names), cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, SqlScripts.AppendHelpers(names), cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, SqlScripts.AppendEvents(names), cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        // Npgsql loads types on the data source's first connection, which may predate the enums created above.
        await dataSource.ReloadTypesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Drops the schema, including all events. Intended for tests and tooling.
    /// </summary>
    public static async Task DropAsync(
        NpgsqlDataSource dataSource,
        PostgresEventStoreOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgresEventStoreOptions();
        var names = new SchemaObjectNames(options.Schema);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(connection, $"DROP SCHEMA IF EXISTS {names.Schema} CASCADE;", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}