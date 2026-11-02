using DomainBlocks.Testing.Integration.MongoDB;
using NUnit.Framework;

namespace DomainBlocks.MongoDB.Sequencing.Benchmarks;

[SetUpFixture]
public class SetUpFixture
{
    [OneTimeSetUp]
    public Task OneTimeSetUp() => MongoTestEnvironment.StartAsync();

    [OneTimeTearDown]
    public Task OneTimeTearDown() => MongoTestEnvironment.StopAsync();
}