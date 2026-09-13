using DomainBlocks.Testing.Integration.PostgreSQL;
using NUnit.Framework;

namespace DomainBlocks.EventStore.PostgreSQL.Benchmarks;

[SetUpFixture]
public class SetUpFixture
{
    [OneTimeSetUp]
    public Task OneTimeSetUp() => PostgresTestEnvironment.StartAsync();

    [OneTimeTearDown]
    public Task OneTimeTearDown() => PostgresTestEnvironment.StopAsync();
}