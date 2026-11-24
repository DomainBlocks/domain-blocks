using DomainBlocks.Benchmarking;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace DomainBlocks.EventStore.PostgreSQL.Benchmarks;

[SetUpFixture]
public class SetUpFixture
{
    private NetworkLatencyProxy? _proxy;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await PostgresTestEnvironment.StartAsync();

        _proxy = await NetworkLatencyProxy.StartFromEnvironmentAsync(
            PostgresTestEnvironment.Server.Network,
            PostgresServer.NetworkAlias,
            PostgreSqlBuilder.PostgreSqlPort);

        if (_proxy is not null)
            await PostgresTestEnvironment.UseProxyAsync(_proxy.Hostname, _proxy.Port);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        // The proxy is on the server's network, which can only be removed once nothing is attached to it.
        if (_proxy is not null)
            await _proxy.DisposeAsync();

        await PostgresTestEnvironment.StopAsync();
    }
}