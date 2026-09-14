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

        var builder = new NpgsqlDataSourceBuilder(_server.ConnectionString);
        builder.ConnectionStringBuilder.MaxAutoPrepare = 16;

        // The replication connection is built from the data source's connection string, which only carries the
        // password when security info is persisted.
        builder.ConnectionStringBuilder.PersistSecurityInfo = true;

        ConnectionString = builder.ConnectionStringBuilder.ConnectionString;
        DataSource = builder.Build();

        await using var command = DataSource.CreateCommand("SHOW wal_level");

        if (await command.ExecuteScalarAsync() is not "logical")
            throw new InvalidOperationException("The test server must be started with wal_level=logical.");
    }

    public static async Task StopAsync()
    {
        LoggerFactory.Dispose();
        await DataSource.DisposeAsync();

        if (_server is not null)
            await _server.DisposeAsync();
    }
}