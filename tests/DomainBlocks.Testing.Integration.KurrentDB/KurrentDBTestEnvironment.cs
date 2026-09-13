using KurrentDB.Client;
using Microsoft.Extensions.Logging;
using Testcontainers.KurrentDb;

namespace DomainBlocks.Testing.Integration.KurrentDB;

/// <summary>
/// The KurrentDB server shared by every fixture in a test assembly. An assembly's <c>[SetUpFixture]</c> starts and
/// stops it; fixtures reach it through the static members.
/// </summary>
public static class KurrentDBTestEnvironment
{
    public const string Image = "kurrentplatform/kurrentdb:latest";

    private static KurrentDbContainer? _container;

    public static string ConnectionString { get; private set; } = null!;

    public static KurrentDBClient Client { get; private set; } = null!;

    public static ILoggerFactory LoggerFactory { get; private set; } = null!;

    public static async Task StartAsync()
    {
        _container = new KurrentDbBuilder(Image).Build();
        await _container.StartAsync();

        ConnectionString = _container.GetConnectionString();
        Client = new KurrentDBClient(KurrentDBClientSettings.Create(ConnectionString));
        LoggerFactory = TestLoggerFactory.Create();
    }

    public static async Task StopAsync()
    {
        LoggerFactory.Dispose();
        await Client.DisposeAsync();

        if (_container is not null)
            await _container.DisposeAsync();
    }
}