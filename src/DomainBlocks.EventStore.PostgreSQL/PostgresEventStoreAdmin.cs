using DomainBlocks.EventStore.Filtering;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL;

public static class PostgresEventStoreAdmin
{
    /// <summary>
    /// Creates the schema, types, tables, sequence row, append functions and publication used by the event store if
    /// they do not already exist. Safe to call on every start-up and from multiple processes concurrently. The data
    /// source's type cache is reloaded afterwards, so a store created over the same data source can use the types.
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

        var names = new SchemaObjectNames(options.Schema, options.SubscriptionFilter);
        var rowFilter = PostgresFilterTranslator.ToPublicationRowFilter(options.SubscriptionFilter);

        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            await ExecuteAsync(connection, SqlScripts.Schema(names), cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, SqlScripts.AppendHelpers(names), cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, SqlScripts.AppendEvents(names), cancellationToken).ConfigureAwait(false);

            if (adminOptions.CreatePublication)
            {
                await EnsurePublicationAsync(connection, names, options.SubscriptionFilter, rowFilter, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        // Npgsql loads the database's types when a data source opens its first physical connection, which is before
        // the enums above exist when the same data source initializes the schema. Connections opened from now on see
        // them; the initializing connection has been returned to the pool.
        await dataSource.ReloadTypesAsync(cancellationToken).ConfigureAwait(false);
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
        var names = new SchemaObjectNames(options.Schema);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Every publication of the schema, whatever filter each was made for: the name that one has without a filter,
        // or that and the eight digits of a hash. Anything else that the name begins is someone else's.
        var publications = new List<string>();

        await using (var command = new NpgsqlCommand(
                         "SELECT pubname FROM pg_publication WHERE pubname = $1 OR pubname ~ $2",
                         connection))
        {
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = names.PublicationPrefix });
            command.Parameters.Add(
                new NpgsqlParameter<string> { TypedValue = $"^{names.PublicationPrefix}_[0-9a-f]{{8}}$" });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                publications.Add(reader.GetString(0));
        }

        var dropPublications = string.Concat(publications.Select(x => $"DROP PUBLICATION IF EXISTS \"{x}\"; "));

        await ExecuteAsync(
                connection,
                $"{dropPublications}DROP SCHEMA IF EXISTS {names.Schema} CASCADE;",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsurePublicationAsync(
        NpgsqlConnection connection,
        SchemaObjectNames names,
        EventFilter subscriptionFilter,
        string? rowFilter,
        CancellationToken cancellationToken)
    {
        await using var find = new NpgsqlCommand(
            "SELECT obj_description(oid, 'pg_publication') FROM pg_publication WHERE pubname = $1",
            connection);

        find.Parameters.Add(new NpgsqlParameter<string> { TypedValue = names.Publication });

        await using (var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                // A publication is named after a hash of its filter. It says what the filter was, to be sure of it.
                var description = reader.IsDBNull(0) ? null : reader.GetString(0);

                if (rowFilter is not null && description != subscriptionFilter.ToString())
                {
                    throw new InvalidOperationException(
                        $"The publication '{names.Publication}' exists, but not for the subscription filter " +
                        $"'{subscriptionFilter}'. It says it is for '{description ?? "<nothing>"}'.");
                }

                return;
            }
        }

        if (rowFilter is null)
        {
            await ExecuteAsync(
                    connection,
                    $"CREATE PUBLICATION {names.Publication} FOR TABLE {names.EventLog} WITH (publish = 'insert')",
                    cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        if (connection.PostgreSqlVersion.Major < 15)
        {
            throw new NotSupportedException(
                "A subscription filter takes a publication with a row filter, which PostgreSQL has had since version " +
                $"15. The server is version {connection.PostgreSqlVersion}.");
        }

        var comment = PostgresFilterTranslator.QuoteLiteral(subscriptionFilter.ToString());

        await ExecuteAsync(
                connection,
                $"CREATE PUBLICATION {names.Publication} FOR TABLE {names.EventLog} WHERE ({rowFilter}) " +
                $"WITH (publish = 'insert'); COMMENT ON PUBLICATION {names.Publication} IS {comment}",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}