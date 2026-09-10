using DomainBlocks.Testing;
using DomainBlocks.Testing.Integration.PostgreSQL;
using Microsoft.Extensions.Logging;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

[SetUpFixture]
public class SetUpFixture
{
    public const string LogLevelEnvironmentVariable = "DBX_TEST_LOG_LEVEL";

    private static PostgresServer _server = null!;

    public static NpgsqlDataSource DataSource { get; private set; } = null!;

    public static string ConnectionString { get; private set; } = null!;

    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _server = await PostgresServer.StartAsync();

        // Set DBX_TEST_LOG_LEVEL=Trace to see per-batch append logging, e.g. when investigating benchmark results.
        var logLevel = Enum.TryParse<LogLevel>(
            Environment.GetEnvironmentVariable(LogLevelEnvironmentVariable), true, out var configured)
            ? configured
            : LogLevel.Debug;

        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(x => x
            .AddProvider(new NUnitLoggerProvider(logLevel))
            .SetMinimumLevel(logLevel));

        var builder = new NpgsqlDataSourceBuilder(_server.ConnectionString);
        builder.ConnectionStringBuilder.MaxAutoPrepare = 16;

        // The replication connection is built from the data source's connection string, which only carries the
        // password when security info is persisted.
        builder.ConnectionStringBuilder.PersistSecurityInfo = true;

        ConnectionString = builder.ConnectionStringBuilder.ConnectionString;
        DataSource = builder.Build();

        await using var command = DataSource.CreateCommand("SHOW wal_level");
        var walLevel = await command.ExecuteScalarAsync();
        walLevel.ShouldBe("logical", "The test server must be started with wal_level=logical");
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        LoggerFactory.Dispose();
        await DataSource.DisposeAsync();
        await _server.DisposeAsync();
    }
}