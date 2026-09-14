using DomainBlocks.Testing.Integration.EventStore.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.EventSourcing.Tests.Integration;

[SetUpFixture]
public class SetUpFixture
{
    [OneTimeSetUp]
    public Task OneTimeSetUp() => MongoTestEnvironment.StartAsync();

    [OneTimeTearDown]
    public Task OneTimeTearDown() => MongoTestEnvironment.StopAsync();
}