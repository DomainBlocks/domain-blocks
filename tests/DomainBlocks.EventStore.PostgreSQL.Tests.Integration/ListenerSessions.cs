using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Npgsql;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

/// <summary>
/// Finds the sessions that listen for a schema's appends. A session that listens runs LISTEN as its last query.
/// </summary>
/// <remarks>
/// It queries through the test environment's own pool. Through the store's pool it could reuse a connection that the
/// store had returned, which would reset the connection and change its last query.
/// </remarks>
internal static class ListenerSessions
{
    public static async Task<int> CountAsync(string schema, CancellationToken cancellationToken)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND query = $1");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ListenQuery(schema) });

        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// Terminates the sessions that listen for the schema's appends, and returns how many there were.
    /// </summary>
    public static async Task<int> TerminateAsync(string schema, CancellationToken cancellationToken)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity " +
            "WHERE datname = current_database() AND query = $1");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = ListenQuery(schema) });

        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// Waits until the given number of sessions listen for the schema's appends.
    /// </summary>
    public static async Task WaitForCountAsync(string schema, int count, CancellationToken cancellationToken)
    {
        while (await CountAsync(schema, cancellationToken) != count)
            await Task.Delay(20, cancellationToken);
    }

    private static string ListenQuery(string schema) => $"LISTEN \"{new SchemaObjectNames(schema).AppendChannel}\"";
}