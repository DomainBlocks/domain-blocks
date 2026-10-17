using Microsoft.Extensions.Logging;
using Npgsql;

namespace DomainBlocks.Testing.Integration.EventStore.PostgreSQL;

/// <summary>
/// The PostgreSQL server shared by every fixture in a test assembly. An assembly's <c>[SetUpFixture]</c> starts and
/// stops it, and fixtures reach it through the static members.
/// </summary>
public static class PostgresTestEnvironment
{
    private static PostgresServer? _server;

    /// <summary>
    /// A data source for administration and raw SQL against any schema on the server. A fixture's store uses the
    /// fixture's own data source instead. See <see cref="PostgresEventStoreTestHarness.DataSource"/>.
    /// </summary>
    public static NpgsqlDataSource DataSource { get; private set; } = null!;

    /// <summary>
    /// The connection string that data sources for the server are built from, including the password.
    /// </summary>
    public static string ConnectionString { get; private set; } = null!;

    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    public static async Task StartAsync()
    {
        _server = await PostgresServer.StartAsync();
        LoggerFactory = TestLoggerFactory.Create();

        var connectionString = new NpgsqlConnectionStringBuilder(_server.ConnectionString)
        {
            MaxAutoPrepare = 16
        };

        ConnectionString = connectionString.ConnectionString;
        DataSource = CreateDataSource();
    }

    /// <summary>
    /// Builds a data source for the server with the shared connection settings. What a data source is built with, such
    /// as type mappings, is fixed for its lifetime, so a fixture that needs settings of its own builds its own.
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