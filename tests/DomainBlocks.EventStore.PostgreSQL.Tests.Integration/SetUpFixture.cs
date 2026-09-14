using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using NUnit.Framework;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

[SetUpFixture]
public class SetUpFixture
{
    [OneTimeSetUp]
    public Task OneTimeSetUp() => PostgresTestEnvironment.StartAsync();

    [OneTimeTearDown]
    public Task OneTimeTearDown() => PostgresTestEnvironment.StopAsync();
}