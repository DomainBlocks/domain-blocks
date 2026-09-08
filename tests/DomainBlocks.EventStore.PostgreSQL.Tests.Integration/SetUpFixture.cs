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
    private static PostgresServer _server = null!;

    public static NpgsqlDataSource DataSource { get; private set; } = null!;

    public static string ConnectionString { get; private set; } = null!;

    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _server = await PostgresServer.StartAsync();

        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(x => x
            .AddProvider(new NUnitLoggerProvider())
            .SetMinimumLevel(LogLevel.Debug));

        var builder = new NpgsqlDataSourceBuilder(_server.ConnectionString);
        builder.ConnectionStringBuilder.MaxAutoPrepare = 16;

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
