using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Testcontainers.KurrentDb;

namespace DomainBlocks.EventStore.KurrentDB.Tests.Integration;

[SetUpFixture]
public class SetUpFixture
{
    private static KurrentDbContainer _kurrentDb = null!;

    public static string KurrentDBConnectionString { get; private set; } = null!;

    public static KurrentDBClient KurrentDBClient { get; private set; } = null!;

    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _kurrentDb = new KurrentDbBuilder("kurrentplatform/kurrentdb:latest").Build();
        await _kurrentDb.StartAsync();

        KurrentDBConnectionString = _kurrentDb.GetConnectionString();

        KurrentDBClient = new KurrentDBClient(KurrentDBClientSettings.Create(KurrentDBConnectionString));

        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(x => x
            .AddSimpleConsole(opt =>
            {
                opt.IncludeScopes = true;
                opt.TimestampFormat = "HH:mm:ss.fff ";
                opt.SingleLine = true;
            })
            .SetMinimumLevel(LogLevel.Debug));
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        LoggerFactory.Dispose();

        await KurrentDBClient.DisposeAsync();

        await _kurrentDb.DisposeAsync();
    }
}