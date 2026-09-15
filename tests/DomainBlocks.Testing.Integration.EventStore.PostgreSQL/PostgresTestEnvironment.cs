using Microsoft.Extensions.Logging;
using Npgsql;

namespace DomainBlocks.Testing.Integration.EventStore.PostgreSQL;

/// <summary>
/// The PostgreSQL server shared by every fixture in a test assembly. An assembly's <c>[SetUpFixture]</c> starts and
/// stops it; fixtures reach it through the static members.
/// </summary>
public static class PostgresTestEnvironment
{
    private static PostgresServer? _server;

    /// <summary>
    /// A data source for administration and raw SQL against any schema on the server. A fixture's store uses the
    /// fixture's own data source, see <see cref="PostgresEventStoreTestHarness.DataSource"/>.
    /// </summary>
    public static NpgsqlDataSource DataSource { get; private set; } = null!;

    /// <summary>
    /// The data source's connection string with the password included, for replication connections.
    /// </summary>
    public static string ConnectionString { get; private set; } = null!;

    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    public static async Task StartAsync()
    {
        _server = await PostgresServer.StartAsync();
        LoggerFactory = TestLoggerFactory.Create();

        var connectionString = new NpgsqlConnectionStringBuilder(_server.ConnectionString)
        {
            // The replication connection is built from the data source's connection string, which only carries the
            // password when security info is persisted.
            PersistSecurityInfo = true,

            MaxAutoPrepare = 16
        };

        ConnectionString = connectionString.ConnectionString;
        DataSource = CreateDataSource();

        await using var command = DataSource.CreateCommand("SHOW wal_level");

        if (await command.ExecuteScalarAsync() is not "logical")
            throw new InvalidOperationException("The test server must be started with wal_level=logical.");
    }

    /// <summary>
    /// Builds a data source for the server with the shared connection settings. What a data source is built with,
    /// such as type mappings, is fixed for its lifetime, so a fixture that needs settings of its own builds its own.
    /// </summary>
    public static NpgsqlDataSource CreateDataSource(Action<NpgsqlDataSourceBuilder>? configure = null)
    {
        var builder = new NpgsqlDataSourceBuilder(ConnectionString);
        configure?.Invoke(builder);
        return builder.Build();
    }

    public static async Task StopAsync()
    {
        LoggerFactory.Dispose();
        await DataSource.DisposeAsync();

        if (_server is not null)
            await _server.DisposeAsync();
    }
}